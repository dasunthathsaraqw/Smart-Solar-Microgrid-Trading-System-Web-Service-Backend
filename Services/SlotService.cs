/**
 * File: SlotService.cs
 * Purpose: Implements energy booking slot CRUD against MongoDB, enforcing timing rules
 *          (future date, 30-day window, 30min-8hr duration), station-active checks,
 *          per-station overlap detection, and the booked-slot modification block.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class SlotService : ISlotService
{
    private readonly IMongoDbService _db;

    public SlotService(IMongoDbService db)
    {
        _db = db;
    }

    // Returns slots optionally filtered by station and by derived status ("available" | "booked" | "past").
    public async Task<List<SlotResponse>> GetAllAsync(string? stationId, string? status)
    {
        var filters = new List<FilterDefinition<EnergyBookingSlot>>();

        if (!string.IsNullOrEmpty(stationId))
        {
            filters.Add(Builders<EnergyBookingSlot>.Filter.Eq(s => s.StationId, stationId));
        }

        var now = DateTime.UtcNow;
        switch (status?.ToLowerInvariant())
        {
            case "available":
                filters.Add(Builders<EnergyBookingSlot>.Filter.Eq(s => s.IsBooked, false));
                filters.Add(Builders<EnergyBookingSlot>.Filter.Gt(s => s.EndTime, now));
                break;
            case "booked":
                filters.Add(Builders<EnergyBookingSlot>.Filter.Eq(s => s.IsBooked, true));
                break;
            case "past":
                filters.Add(Builders<EnergyBookingSlot>.Filter.Lte(s => s.EndTime, now));
                break;
        }

        var filter = filters.Count > 0 ? Builders<EnergyBookingSlot>.Filter.And(filters) : FilterDefinition<EnergyBookingSlot>.Empty;
        var slots = await _db.Slots.Find(filter).SortBy(s => s.StartTime).ToListAsync();
        return slots.Select(ToResponse).ToList();
    }

    // Looks up a single slot by its MongoDB ObjectId.
    public async Task<SlotResponse?> GetByIdAsync(string id)
    {
        var slot = await _db.Slots.Find(s => s.Id == id).FirstOrDefaultAsync();
        return slot is null ? null : ToResponse(slot);
    }

    // Returns all slots belonging to a given station, ordered by start time.
    public async Task<List<SlotResponse>> GetByStationAsync(string stationId)
    {
        var slots = await _db.Slots.Find(s => s.StationId == stationId).SortBy(s => s.StartTime).ToListAsync();
        return slots.Select(ToResponse).ToList();
    }

    // Creates a single slot after validating the station, timing rules and overlap.
    public async Task<SlotResponse> CreateAsync(CreateSlotRequest request, string createdBy)
    {
        var station = await GetActiveStationOrThrowAsync(request.StationId);

        ValidateTiming(request.StartTime, request.EndTime);

        if (await HasOverlapAsync(request.StationId, request.StartTime, request.EndTime))
        {
            throw new SlotOverlapException("Slot overlaps with an existing slot");
        }

        var slot = new EnergyBookingSlot
        {
            StationId = request.StationId,
            StationName = station.StationName,
            SlotDate = request.SlotDate.Date,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            CapacityKw = request.CapacityKw,
            IsBooked = false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy,
        };

        await _db.Slots.InsertOneAsync(slot);
        return ToResponse(slot);
    }

    // Generates fixed-interval slots across a single day, silently skipping any that would
    // overlap an existing slot (or one already generated earlier in this same batch).
    public async Task<List<SlotResponse>> BulkCreateAsync(BulkCreateSlotRequest request, string createdBy)
    {
        var station = await GetActiveStationOrThrowAsync(request.StationId);

        if (request.EndTime <= request.StartTime)
        {
            throw new InvalidOperationException("Day end time must be after day start time");
        }

        var slotDate = request.SlotDate.Date;
        var dayStart = slotDate.Add(request.StartTime);
        var dayEnd = slotDate.Add(request.EndTime);
        var duration = TimeSpan.FromMinutes(request.SlotDurationMinutes);
        var now = DateTime.UtcNow;
        var maxFuture = now.AddDays(30);

        var created = new List<EnergyBookingSlot>();
        var cursor = dayStart;

        while (cursor.Add(duration) <= dayEnd)
        {
            var slotStart = cursor;
            var slotEnd = cursor.Add(duration);
            cursor = slotEnd;

            // Skip slots that would be in the past, beyond the 30-day window, or that overlap
            // an existing slot or one already queued earlier in this same batch.
            if (slotStart <= now || slotStart > maxFuture)
            {
                continue;
            }

            if (created.Any(s => s.StartTime < slotEnd && s.EndTime > slotStart))
            {
                continue;
            }

            if (await HasOverlapAsync(request.StationId, slotStart, slotEnd))
            {
                continue;
            }

            created.Add(new EnergyBookingSlot
            {
                StationId = request.StationId,
                StationName = station.StationName,
                SlotDate = slotDate,
                StartTime = slotStart,
                EndTime = slotEnd,
                CapacityKw = request.CapacityPerSlotKw,
                IsBooked = false,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy,
            });
        }

        if (created.Count > 0)
        {
            await _db.Slots.InsertManyAsync(created);
        }

        return created.Select(ToResponse).ToList();
    }

    // Updates timing/capacity of an unbooked slot; re-validates timing and overlap if timing changed.
    public async Task<SlotResponse?> UpdateAsync(string id, UpdateSlotRequest request)
    {
        var slot = await _db.Slots.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (slot is null)
        {
            return null;
        }

        if (slot.IsBooked)
        {
            throw new InvalidOperationException("Cannot modify a booked slot");
        }

        var newStart = request.StartTime ?? slot.StartTime;
        var newEnd = request.EndTime ?? slot.EndTime;

        if (request.StartTime.HasValue || request.EndTime.HasValue)
        {
            ValidateTiming(newStart, newEnd);

            if (await HasOverlapAsync(slot.StationId, newStart, newEnd, id))
            {
                throw new SlotOverlapException("Slot overlaps with an existing slot");
            }
        }

        var updates = new List<UpdateDefinition<EnergyBookingSlot>>();

        if (request.StartTime.HasValue)
        {
            updates.Add(Builders<EnergyBookingSlot>.Update.Set(s => s.StartTime, request.StartTime.Value));
        }

        if (request.EndTime.HasValue)
        {
            updates.Add(Builders<EnergyBookingSlot>.Update.Set(s => s.EndTime, request.EndTime.Value));
        }

        if (request.CapacityKw.HasValue)
        {
            updates.Add(Builders<EnergyBookingSlot>.Update.Set(s => s.CapacityKw, request.CapacityKw.Value));
        }

        if (updates.Count > 0)
        {
            updates.Add(Builders<EnergyBookingSlot>.Update.Set(s => s.UpdatedAt, DateTime.UtcNow));
            await _db.Slots.UpdateOneAsync(s => s.Id == id, Builders<EnergyBookingSlot>.Update.Combine(updates));
        }

        var updated = await _db.Slots.Find(s => s.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Deletes an unbooked slot. Booked slots cannot be deleted.
    public async Task<bool> DeleteAsync(string id)
    {
        var slot = await _db.Slots.Find(s => s.Id == id).FirstOrDefaultAsync();
        if (slot is null)
        {
            return false;
        }

        if (slot.IsBooked)
        {
            throw new InvalidOperationException("Cannot modify a booked slot");
        }

        var result = await _db.Slots.DeleteOneAsync(s => s.Id == id);
        return result.DeletedCount > 0;
    }

    // Checks whether the given time window overlaps any existing slot for the same station.
    public async Task<bool> HasOverlapAsync(string stationId, DateTime startTime, DateTime endTime, string? excludeId = null)
    {
        var filter = Builders<EnergyBookingSlot>.Filter.And(
            Builders<EnergyBookingSlot>.Filter.Eq(s => s.StationId, stationId),
            Builders<EnergyBookingSlot>.Filter.Lt(s => s.StartTime, endTime),
            Builders<EnergyBookingSlot>.Filter.Gt(s => s.EndTime, startTime)
        );

        if (!string.IsNullOrEmpty(excludeId))
        {
            filter = Builders<EnergyBookingSlot>.Filter.And(filter, Builders<EnergyBookingSlot>.Filter.Ne(s => s.Id, excludeId));
        }

        return await _db.Slots.Find(filter).AnyAsync();
    }

    // Loads the station for a slot request, failing if it doesn't exist or is deactivated.
    private async Task<SolarStationInfo> GetActiveStationOrThrowAsync(string stationId)
    {
        var station = await _db.Stations.Find(s => s.Id == stationId).FirstOrDefaultAsync();
        if (station is null || !station.IsActive)
        {
            throw new InvalidOperationException("Station not found or inactive");
        }

        return station;
    }

    // Enforces the shared timing rules: must be in the future, within 30 days, and 30min-8hr long.
    private static void ValidateTiming(DateTime startTime, DateTime endTime)
    {
        var now = DateTime.UtcNow;

        if (startTime <= now)
        {
            throw new InvalidOperationException("Slot start time must be in the future");
        }

        if (startTime > now.AddDays(30))
        {
            throw new InvalidOperationException("Slot cannot be created more than 30 days in the future");
        }

        if (endTime <= startTime)
        {
            throw new InvalidOperationException("End time must be after start time");
        }

        var duration = endTime - startTime;
        if (duration < TimeSpan.FromMinutes(30) || duration > TimeSpan.FromHours(8))
        {
            throw new InvalidOperationException("Slot duration must be between 30 minutes and 8 hours");
        }
    }

    // Maps an EnergyBookingSlot document to its public response shape.
    private static SlotResponse ToResponse(EnergyBookingSlot slot)
    {
        return new SlotResponse
        {
            Id = slot.Id,
            StationId = slot.StationId,
            StationName = slot.StationName,
            SlotDate = slot.SlotDate,
            StartTime = slot.StartTime,
            EndTime = slot.EndTime,
            CapacityKw = slot.CapacityKw,
            IsBooked = slot.IsBooked,
            CreatedAt = slot.CreatedAt,
            CreatedBy = slot.CreatedBy,
            UpdatedAt = slot.UpdatedAt,
        };
    }
}
