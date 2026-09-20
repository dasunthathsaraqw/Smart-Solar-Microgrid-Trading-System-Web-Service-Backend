/**
 * File: StationService.cs
 * Purpose: Implements microgrid station CRUD against MongoDB, including unique-name enforcement
 *          and the deactivation block rule (blocked while an "Approved" reservation exists).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class StationService : IStationService
{
    private readonly IMongoDbService _db;
    private readonly IReservationService _reservationService;

    public StationService(IMongoDbService db, IReservationService reservationService)
    {
        _db = db;
        _reservationService = reservationService;
    }

    // Returns stations filtered by "active" | "deactivated", or all when status is null/unknown.
    public async Task<List<StationResponse>> GetAllAsync(string? status)
    {
        FilterDefinition<SolarStationInfo> filter = status?.ToLowerInvariant() switch
        {
            "active" => Builders<SolarStationInfo>.Filter.Eq(s => s.IsActive, true),
            "deactivated" => Builders<SolarStationInfo>.Filter.Eq(s => s.IsActive, false),
            _ => FilterDefinition<SolarStationInfo>.Empty,
        };

        var stations = await _db.Stations.Find(filter).SortByDescending(s => s.CreatedAt).ToListAsync();
        return stations.Select(ToResponse).ToList();
    }

    // Looks up a single station by its MongoDB ObjectId.
    public async Task<StationResponse?> GetByIdAsync(string id)
    {
        var station = await _db.Stations.Find(s => s.Id == id).FirstOrDefaultAsync();
        return station is null ? null : ToResponse(station);
    }

    // Creates a new station, enforcing a case-insensitive unique station name.
    public async Task<StationResponse> CreateAsync(CreateStationRequest request, string createdBy)
    {
        if (await NameExistsAsync(request.StationName))
        {
            throw new InvalidOperationException("Station name already exists");
        }

        var station = new SolarStationInfo
        {
            StationName = request.StationName,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CapacityKw = request.CapacityKw,
            AvailableSlots = request.AvailableSlots,
            Schedule = request.Schedule,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };

        await _db.Stations.InsertOneAsync(station);
        return ToResponse(station);
    }

    // Updates the editable fields of a station. Re-checks name uniqueness if it changed.
    public async Task<StationResponse?> UpdateAsync(string id, UpdateStationRequest request)
    {
        var station = await _db.Stations.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (station is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.StationName)
            && !string.Equals(request.StationName, station.StationName, StringComparison.OrdinalIgnoreCase)
            && await NameExistsAsync(request.StationName, id))
        {
            throw new InvalidOperationException("Station name already exists");
        }

        var updates = new List<UpdateDefinition<SolarStationInfo>>();

        if (!string.IsNullOrWhiteSpace(request.StationName))
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.StationName, request.StationName));
        }

        if (request.Latitude.HasValue)
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.Latitude, request.Latitude.Value));
        }

        if (request.Longitude.HasValue)
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.Longitude, request.Longitude.Value));
        }

        if (request.CapacityKw.HasValue)
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.CapacityKw, request.CapacityKw.Value));
        }

        if (request.AvailableSlots.HasValue)
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.AvailableSlots, request.AvailableSlots.Value));
        }

        if (!string.IsNullOrWhiteSpace(request.Schedule))
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.Schedule, request.Schedule));
        }

        if (updates.Count > 0)
        {
            updates.Add(Builders<SolarStationInfo>.Update.Set(s => s.UpdatedAt, DateTime.UtcNow));
            await _db.Stations.UpdateOneAsync(s => s.Id == id, Builders<SolarStationInfo>.Update.Combine(updates));
        }

        var updated = await _db.Stations.Find(s => s.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Deactivates a station unless it has an active ("Approved") reservation, in which case it is blocked.
    public async Task<(bool Success, string? Error)> DeactivateAsync(string id)
    {
        var station = await _db.Stations.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (station is null)
        {
            return (false, "Station not found");
        }

        if (await HasActiveReservationsAsync(id))
        {
            return (false, "Cannot deactivate: active reservations exist");
        }

        await _db.Stations.UpdateOneAsync(
            s => s.Id == id,
            Builders<SolarStationInfo>.Update.Set(s => s.IsActive, false).Set(s => s.UpdatedAt, DateTime.UtcNow)
        );

        return (true, null);
    }

    // Reactivates a previously deactivated station.
    public async Task<bool> ReactivateAsync(string id)
    {
        var result = await _db.Stations.UpdateOneAsync(
            s => s.Id == id,
            Builders<SolarStationInfo>.Update.Set(s => s.IsActive, true).Set(s => s.UpdatedAt, DateTime.UtcNow)
        );

        return result.MatchedCount > 0;
    }

    // Checks whether a station name is already in use (case-insensitive), optionally excluding one station's own id.
    public async Task<bool> NameExistsAsync(string stationName, string? excludeId = null)
    {
        var nameFilter = Builders<SolarStationInfo>.Filter.Regex(
            s => s.StationName,
            new BsonRegularExpression($"^{Regex.Escape(stationName)}$", "i")
        );

        var filter = string.IsNullOrEmpty(excludeId)
            ? nameFilter
            : Builders<SolarStationInfo>.Filter.And(nameFilter, Builders<SolarStationInfo>.Filter.Ne(s => s.Id, excludeId));

        return await _db.Stations.Find(filter).AnyAsync();
    }

    // Checks whether the station has any reservation whose status is exactly "Approved".
    // Delegates to ReservationService, the single source of truth for reservation rules.
    public async Task<bool> HasActiveReservationsAsync(string stationId)
    {
        return await _reservationService.HasActiveReservationsAsync(stationId);
    }

    // Maps a SolarStationInfo document to its public response shape.
    private static StationResponse ToResponse(SolarStationInfo station)
    {
        return new StationResponse
        {
            Id = station.Id,
            StationName = station.StationName,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            CapacityKw = station.CapacityKw,
            AvailableSlots = station.AvailableSlots,
            Schedule = station.Schedule,
            IsActive = station.IsActive,
            CreatedAt = station.CreatedAt,
            CreatedBy = station.CreatedBy,
            UpdatedAt = station.UpdatedAt,
        };
    }
}
