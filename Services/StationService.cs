/**
 * File: StationService.cs
 * Purpose: Implements station CRUD, nearby search, slot availability counts and lifecycle rules.
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

    // Initializes station operations with MongoDB access and reservation lifecycle checks.
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

    // Returns nearest active stations with one batched query for their next-seven-day available-slot counts.
    public async Task<List<NearbyStationResponse>> GetNearbyAsync(
        double latitude,
        double longitude,
        double radiusKm,
        int limit)
    {
        if (latitude < -90 || latitude > 90)
        {
            throw new InvalidOperationException("Latitude must be between -90 and 90.");
        }

        if (longitude < -180 || longitude > 180)
        {
            throw new InvalidOperationException("Longitude must be between -180 and 180.");
        }

        if (radiusKm <= 0 || radiusKm > 500)
        {
            throw new InvalidOperationException("Radius must be greater than 0 and no more than 500 km.");
        }

        if (limit <= 0)
        {
            throw new InvalidOperationException("Limit must be greater than 0.");
        }

        // A production-scale dataset should use a MongoDB 2dsphere index and $geoNear. Service-layer
        // haversine keeps this small dataset's business logic centralized without an index migration.
        var activeStations = await _db.Stations.Find(s => s.IsActive).ToListAsync();
        var nearbyStations = activeStations
            .Select(station => new
            {
                Station = station,
                DistanceKm = CalculateDistanceKm(latitude, longitude, station.Latitude, station.Longitude),
            })
            .Where(result => result.DistanceKm <= radiusKm)
            .OrderBy(result => result.DistanceKm)
            .Take(limit)
            .ToList();

        if (nearbyStations.Count == 0)
        {
            return [];
        }

        var stationIds = nearbyStations.Select(result => result.Station.Id).ToList();
        var now = DateTime.UtcNow;
        var sevenDaysFromNow = now.AddDays(7);
        var slotFilter = Builders<EnergyBookingSlot>.Filter.And(
            Builders<EnergyBookingSlot>.Filter.In(slot => slot.StationId, stationIds),
            Builders<EnergyBookingSlot>.Filter.Eq(slot => slot.IsBooked, false),
            Builders<EnergyBookingSlot>.Filter.Gt(slot => slot.StartTime, now),
            Builders<EnergyBookingSlot>.Filter.Lte(slot => slot.StartTime, sevenDaysFromNow));

        var availableSlots = await _db.Slots.Find(slotFilter).ToListAsync();
        var slotCounts = availableSlots
            .GroupBy(slot => slot.StationId)
            .ToDictionary(group => group.Key, group => group.Count());

        return nearbyStations.Select(result => ToNearbyResponse(
            result.Station,
            result.DistanceKm,
            slotCounts.GetValueOrDefault(result.Station.Id))).ToList();
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

        try
        {
            await _db.Stations.InsertOneAsync(station);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == 11000)
        {
            throw new InvalidOperationException("Station name already exists", ex);
        }
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
            try
            {
                await _db.Stations.UpdateOneAsync(s => s.Id == id, Builders<SolarStationInfo>.Update.Combine(updates));
            }
            catch (MongoWriteException ex) when (ex.WriteError.Code == 11000)
            {
                throw new InvalidOperationException("Station name already exists", ex);
            }
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

    // Maps a station and its computed search values to the nearby-station response shape.
    private static NearbyStationResponse ToNearbyResponse(
        SolarStationInfo station,
        double distanceKm,
        int availableSlotCount)
    {
        return new NearbyStationResponse
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
            DistanceKm = Math.Round(distanceKm, 2, MidpointRounding.AwayFromZero),
            AvailableSlotCount = availableSlotCount,
        };
    }

    // Calculates great-circle distance between two latitude/longitude points with the haversine formula.
    private static double CalculateDistanceKm(
        double originLatitude,
        double originLongitude,
        double destinationLatitude,
        double destinationLongitude)
    {
        const double earthRadiusKm = 6371;

        // Convert latitude and longitude values from degrees to radians.
        var originLatitudeRadians = originLatitude * Math.PI / 180;
        var destinationLatitudeRadians = destinationLatitude * Math.PI / 180;
        var latitudeDifferenceRadians = (destinationLatitude - originLatitude) * Math.PI / 180;
        var longitudeDifferenceRadians = (destinationLongitude - originLongitude) * Math.PI / 180;

        // Apply the haversine formula to obtain the central angle between the coordinates.
        var haversine = Math.Pow(Math.Sin(latitudeDifferenceRadians / 2), 2)
            + Math.Cos(originLatitudeRadians)
            * Math.Cos(destinationLatitudeRadians)
            * Math.Pow(Math.Sin(longitudeDifferenceRadians / 2), 2);
        var boundedHaversine = Math.Clamp(haversine, 0, 1);
        var centralAngle = 2 * Math.Atan2(
            Math.Sqrt(boundedHaversine),
            Math.Sqrt(1 - boundedHaversine));

        // Convert the central angle into surface distance using Earth's mean radius.
        return earthRadiusKm * centralAngle;
    }
}
