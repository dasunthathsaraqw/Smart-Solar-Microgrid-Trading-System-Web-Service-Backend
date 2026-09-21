/**
 * File: ReservationService.cs
 * Purpose: Implements the full reservation lifecycle against MongoDB, enforcing every business
 *          rule server-side: prosumer/station/slot validity, the 7-day booking window, the
 *          12-hour update/cancel notice window (with a Backoffice override on cancel only),
 *          slot locking/unlocking, one-reservation-per-slot-per-prosumer, and QR issuance,
 *          clearing and verification.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class ReservationService : IReservationService
{
    private static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);
    private static readonly TimeSpan TwelveHours = TimeSpan.FromHours(12);
    private static readonly TimeSpan QrToleranceWindow = TimeSpan.FromHours(24);

    private readonly IMongoDbService _db;

    // Initializes reservation operations with MongoDB collections.
    public ReservationService(IMongoDbService db)
    {
        _db = db;
    }

    // Returns reservations optionally filtered by status, station and/or prosumer.
    public async Task<List<ReservationResponse>> GetAllAsync(string? status, string? stationId, string? prosumerNic)
    {
        var filters = new List<FilterDefinition<EnergyReservation>>();

        if (!string.IsNullOrEmpty(status))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.Status, status));
        }

        if (!string.IsNullOrEmpty(stationId))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.StationId, stationId));
        }

        if (!string.IsNullOrEmpty(prosumerNic))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.ProsumerNic, prosumerNic));
        }

        var filter = filters.Count > 0 ? Builders<EnergyReservation>.Filter.And(filters) : FilterDefinition<EnergyReservation>.Empty;
        var reservations = await _db.Reservations.Find(filter).SortByDescending(r => r.CreatedAt).ToListAsync();
        return reservations.Select(ToResponse).ToList();
    }

    // Looks up a single reservation by its MongoDB ObjectId.
    public async Task<ReservationResponse?> GetByIdAsync(string id)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        return reservation is null ? null : ToResponse(reservation);
    }

    // Restricts the existing reservation list to a signed-in prosumer's NIC and optional status.
    public Task<List<ReservationResponse>> GetByProsumerAsync(string prosumerNic, string? status)
    {
        return GetAllAsync(status, null, prosumerNic);
    }

    // Checks a reservation ID and NIC together so ownership failures have one indistinguishable result.
    public async Task<bool> IsOwnedByAsync(string reservationId, string prosumerNic)
    {
        if (!ObjectId.TryParse(reservationId, out _))
        {
            return false;
        }

        return await _db.Reservations.Find(reservation =>
            reservation.Id == reservationId && reservation.ProsumerNic == prosumerNic).AnyAsync();
    }

    // Ignores the client-supplied NIC and delegates the booking to the existing rule-enforcing method.
    public Task<ReservationResponse> CreateForProsumerAsync(CreateReservationRequest request, string prosumerNic)
    {
        request.ProsumerNic = prosumerNic;
        return CreateAsync(request, prosumerNic);
    }

    // Books a slot for a prosumer after validating the prosumer, station, slot and 7-day window.
    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, string createdBy)
    {
        var prosumer = await _db.Prosumers.Find(p => p.Nic == request.ProsumerNic).FirstOrDefaultAsync();
        if (prosumer is null || !prosumer.IsActive)
        {
            throw new InvalidOperationException("Prosumer not found or inactive");
        }

        var station = await _db.Stations.Find(s => s.Id == request.StationId).FirstOrDefaultAsync();
        if (station is null || !station.IsActive)
        {
            throw new InvalidOperationException("Station not found or inactive");
        }

        var slot = await _db.Slots.Find(s => s.Id == request.SlotId).FirstOrDefaultAsync();
        if (slot is null)
        {
            throw new InvalidOperationException("Slot not found");
        }

        if (slot.IsBooked)
        {
            throw new ReservationConflictException("Slot is already booked");
        }

        if (slot.StationId != request.StationId)
        {
            throw new InvalidOperationException("Slot does not belong to this station");
        }

        var now = DateTime.UtcNow;
        if (slot.StartTime <= now)
        {
            throw new InvalidOperationException("Cannot book a past slot");
        }

        if (slot.StartTime - now > SevenDays)
        {
            throw new InvalidOperationException("Reservations must be within 7 days");
        }

        var alreadyReserved = await _db.Reservations.Find(r =>
            r.SlotId == request.SlotId &&
            r.ProsumerNic == request.ProsumerNic &&
            (r.Status == "Pending" || r.Status == "Approved")
        ).AnyAsync();

        if (alreadyReserved)
        {
            throw new InvalidOperationException("Prosumer already has a reservation for this slot");
        }

        var reservation = new EnergyReservation
        {
            ProsumerNic = prosumer.Nic,
            ProsumerName = prosumer.Name,
            StationId = station.Id,
            StationName = station.StationName,
            SlotId = slot.Id,
            SlotStartTime = slot.StartTime,
            SlotEndTime = slot.EndTime,
            CapacityKw = slot.CapacityKw,
            Status = "Pending",
            CreatedAt = now,
            CreatedBy = createdBy,
        };

        await _db.Reservations.InsertOneAsync(reservation);
        await SetSlotBookedAsync(slot.Id, true);

        return ToResponse(reservation);
    }

    // Moves a Pending reservation to a different slot at the same station, subject to the 12-hour notice rule.
    public async Task<ReservationResponse?> UpdateAsync(string id, UpdateReservationRequest request, string updatedBy)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return null;
        }

        if (reservation.Status != "Pending")
        {
            throw new InvalidOperationException("Only pending reservations can be updated");
        }

        var now = DateTime.UtcNow;
        if (reservation.SlotStartTime - now < TwelveHours)
        {
            throw new InvalidOperationException("Updates require at least 12 hours' notice");
        }

        var newSlot = await _db.Slots.Find(s => s.Id == request.NewSlotId).FirstOrDefaultAsync();
        if (newSlot is null)
        {
            throw new InvalidOperationException("Slot not found");
        }

        if (newSlot.IsBooked)
        {
            throw new ReservationConflictException("Slot is already booked");
        }

        if (newSlot.StationId != reservation.StationId)
        {
            throw new InvalidOperationException("Slot does not belong to this station");
        }

        if (newSlot.StartTime <= now)
        {
            throw new InvalidOperationException("Cannot book a past slot");
        }

        if (newSlot.StartTime - now > SevenDays)
        {
            throw new InvalidOperationException("Reservations must be within 7 days");
        }

        await SetSlotBookedAsync(reservation.SlotId, false);
        await SetSlotBookedAsync(newSlot.Id, true);

        var update = Builders<EnergyReservation>.Update
            .Set(r => r.SlotId, newSlot.Id)
            .Set(r => r.SlotStartTime, newSlot.StartTime)
            .Set(r => r.SlotEndTime, newSlot.EndTime)
            .Set(r => r.CapacityKw, newSlot.CapacityKw)
            .Set(r => r.Status, "Pending")
            .Set(r => r.QrToken, (string?)null)
            .Set(r => r.QrGeneratedAt, (DateTime?)null)
            .Set(r => r.UpdatedAt, now);

        await _db.Reservations.UpdateOneAsync(r => r.Id == id, update);

        var updated = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Cancels a Pending or Approved reservation, subject to the 12-hour notice rule.
    // Backoffice users may override the 12-hour rule; Grid Operators may not.
    public async Task<ReservationResponse?> CancelAsync(string id, CancelReservationRequest request, string cancelledBy, bool allowOverride)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return null;
        }

        if (reservation.Status != "Pending" && reservation.Status != "Approved")
        {
            throw new InvalidOperationException("Only pending or approved reservations can be cancelled");
        }

        var now = DateTime.UtcNow;
        if (!allowOverride && reservation.SlotStartTime - now < TwelveHours)
        {
            throw new InvalidOperationException("Cancellations require at least 12 hours' notice");
        }

        await SetSlotBookedAsync(reservation.SlotId, false);

        var update = Builders<EnergyReservation>.Update
            .Set(r => r.Status, "Cancelled")
            .Set(r => r.CancelledAt, now)
            .Set(r => r.CancelledBy, cancelledBy)
            .Set(r => r.CancellationReason, request.Reason)
            .Set(r => r.QrToken, (string?)null)
            .Set(r => r.QrGeneratedAt, (DateTime?)null)
            .Set(r => r.UpdatedAt, now);

        await _db.Reservations.UpdateOneAsync(r => r.Id == id, update);

        var updated = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Approves a Pending reservation and generates its QR token.
    public async Task<ReservationResponse?> ApproveAsync(string id, string approvedBy)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return null;
        }

        if (reservation.Status != "Pending")
        {
            throw new InvalidOperationException("Only pending reservations can be approved");
        }

        var now = DateTime.UtcNow;
        var update = Builders<EnergyReservation>.Update
            .Set(r => r.Status, "Approved")
            .Set(r => r.QrToken, GenerateQrToken())
            .Set(r => r.QrGeneratedAt, now)
            .Set(r => r.ApprovedAt, now)
            .Set(r => r.ApprovedBy, approvedBy)
            .Set(r => r.UpdatedAt, now);

        await _db.Reservations.UpdateOneAsync(r => r.Id == id, update);

        var updated = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Completes an Approved reservation (called after QR verification at the station) and frees its slot for reuse.
    public async Task<ReservationResponse?> CompleteAsync(string id, string completedBy)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return null;
        }

        if (reservation.Status != "Approved")
        {
            throw new InvalidOperationException("Only approved reservations can be completed");
        }

        var now = DateTime.UtcNow;
        await SetSlotBookedAsync(reservation.SlotId, false);

        var update = Builders<EnergyReservation>.Update
            .Set(r => r.Status, "Completed")
            .Set(r => r.CompletedAt, now)
            .Set(r => r.CompletedBy, completedBy)
            .Set(r => r.QrToken, (string?)null)
            .Set(r => r.UpdatedAt, now);

        await _db.Reservations.UpdateOneAsync(r => r.Id == id, update);

        var updated = await _db.Reservations.Find(r => r.Id == id).FirstOrDefaultAsync();
        return ToResponse(updated!);
    }

    // Validates a QR token presented at a station: must exist, be Approved, match the station,
    // and fall within a +/-24 hour tolerance window of the slot's start time.
    public async Task<(bool Valid, ReservationResponse? Reservation, string? Error)> VerifyQrAsync(VerifyQrRequest request)
    {
        var reservation = await _db.Reservations.Find(r => r.QrToken == request.QrToken).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return (false, null, "QR code not recognized");
        }

        if (reservation.Status != "Approved")
        {
            return (false, null, "QR is no longer valid");
        }

        if (reservation.StationId != request.StationId)
        {
            return (false, null, "QR does not belong to this station");
        }

        var now = DateTime.UtcNow;
        if ((reservation.SlotStartTime - now).Duration() > QrToleranceWindow)
        {
            return (false, null, "QR is outside the valid time window");
        }

        return (true, ToResponse(reservation), null);
    }

    // Verifies the QR and atomically claims the Approved-to-Completed transition before freeing its slot.
    public async Task<(bool Success, ReservationResponse? Reservation, string? Error)> ScanAndCompleteAsync(
        VerifyQrRequest request,
        string completedBy)
    {
        var (valid, verifiedReservation, error) = await VerifyQrAsync(request);
        if (!valid || verifiedReservation is null)
        {
            return (false, null, error);
        }

        var now = DateTime.UtcNow;
        var filter = Builders<EnergyReservation>.Filter.And(
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.Id, verifiedReservation.Id),
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.Status, "Approved"),
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.QrToken, request.QrToken));
        var update = Builders<EnergyReservation>.Update
            .Set(reservation => reservation.Status, "Completed")
            .Set(reservation => reservation.CompletedAt, now)
            .Set(reservation => reservation.CompletedBy, completedBy)
            .Set(reservation => reservation.QrToken, (string?)null)
            .Set(reservation => reservation.UpdatedAt, now);

        // Status and QR token are compare-and-swap guards: concurrent scans cannot both complete the same transfer.
        var completed = await _db.Reservations.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<EnergyReservation> { ReturnDocument = ReturnDocument.After });
        if (completed is null)
        {
            return (false, null, "This reservation has already been completed");
        }

        await SetSlotBookedAsync(completed.SlotId, false);
        return (true, ToResponse(completed), null);
    }

    // Returns the QR token for a reservation only while it is Approved; null otherwise.
    public async Task<string?> GetQrTokenAsync(string reservationId)
    {
        var reservation = await _db.Reservations.Find(r => r.Id == reservationId).FirstOrDefaultAsync();
        if (reservation is null || reservation.Status != "Approved")
        {
            return null;
        }

        return reservation.QrToken;
    }

    // Used by StationService to block deactivation while a station has an Approved reservation.
    public async Task<bool> HasActiveReservationsAsync(string stationId)
    {
        return await _db.Reservations.Find(r => r.StationId == stationId && r.Status == "Approved").AnyAsync();
    }

    // Used to guard against deleting/updating a slot that already has a live reservation on it.
    public async Task<bool> SlotHasActiveReservationAsync(string slotId)
    {
        return await _db.Reservations.Find(r => r.SlotId == slotId && (r.Status == "Pending" || r.Status == "Approved")).AnyAsync();
    }

    // Multi-criteria search with sorting and pagination, backing the booking history view.
    // Every criterion is optional and combined with AND; results are read live, never cached.
    public async Task<PagedResult<ReservationResponse>> SearchAsync(ReservationSearchRequest request)
    {
        if (request.DateFrom.HasValue && request.DateTo.HasValue && request.DateFrom > request.DateTo)
        {
            throw new InvalidOperationException("DateFrom must not be after DateTo");
        }

        var sortDir = (request.SortDir ?? "desc").ToLowerInvariant();
        if (sortDir != "asc" && sortDir != "desc")
        {
            throw new InvalidOperationException("SortDir must be 'asc' or 'desc'");
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var filters = new List<FilterDefinition<EnergyReservation>>();

        if (!string.IsNullOrWhiteSpace(request.ProsumerNic))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.ProsumerNic, request.ProsumerNic));
        }

        if (!string.IsNullOrWhiteSpace(request.ProsumerName))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Regex(
                r => r.ProsumerName,
                new BsonRegularExpression(Regex.Escape(request.ProsumerName), "i")
            ));
        }

        if (!string.IsNullOrWhiteSpace(request.StationId))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.StationId, request.StationId));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.Status, request.Status));
        }

        if (request.DateFrom.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Gte(r => r.SlotStartTime, request.DateFrom.Value));
        }

        if (request.DateTo.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Lte(r => r.SlotStartTime, request.DateTo.Value));
        }

        if (request.MinCapacityKw.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Gte(r => r.CapacityKw, request.MinCapacityKw.Value));
        }

        if (request.MaxCapacityKw.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Lte(r => r.CapacityKw, request.MaxCapacityKw.Value));
        }

        var filter = filters.Count > 0 ? Builders<EnergyReservation>.Filter.And(filters) : FilterDefinition<EnergyReservation>.Empty;

        var ascending = sortDir == "asc";
        SortDefinition<EnergyReservation> sort = (request.SortBy?.ToLowerInvariant()) switch
        {
            "capacity" => ascending
                ? Builders<EnergyReservation>.Sort.Ascending(r => r.CapacityKw)
                : Builders<EnergyReservation>.Sort.Descending(r => r.CapacityKw),
            "prosumer" => ascending
                ? Builders<EnergyReservation>.Sort.Ascending(r => r.ProsumerName)
                : Builders<EnergyReservation>.Sort.Descending(r => r.ProsumerName),
            "station" => ascending
                ? Builders<EnergyReservation>.Sort.Ascending(r => r.StationName)
                : Builders<EnergyReservation>.Sort.Descending(r => r.StationName),
            _ => ascending
                ? Builders<EnergyReservation>.Sort.Ascending(r => r.SlotStartTime)
                : Builders<EnergyReservation>.Sort.Descending(r => r.SlotStartTime),
        };

        var totalCount = (int)await _db.Reservations.CountDocumentsAsync(filter);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await _db.Reservations.Find(filter)
            .Sort(sort)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return new PagedResult<ReservationResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1,
        };
    }

    // Prevents cross-account search by replacing the NIC and clearing the optional name filter.
    public Task<PagedResult<ReservationResponse>> SearchForProsumerAsync(
        string prosumerNic,
        ReservationSearchRequest request)
    {
        request.ProsumerNic = prosumerNic;
        request.ProsumerName = null;
        return SearchAsync(request);
    }

    // Computes confirmation text and modifiability from the actual slot time and final status.
    public ReservationActionResponse CreateActionResponse(ReservationResponse reservation, string action)
    {
        var message = action switch
        {
            "Created" => "Reservation created successfully.",
            "Updated" => "Reservation updated successfully.",
            "Cancelled" => "Reservation cancelled successfully.",
            _ => throw new InvalidOperationException("Unsupported reservation action."),
        };

        var exactHoursUntilSlot = (reservation.SlotStartTime - DateTime.UtcNow).TotalHours;
        var roundedHoursUntilSlot = Math.Round(exactHoursUntilSlot, 1, MidpointRounding.AwayFromZero);

        return new ReservationActionResponse
        {
            Action = action,
            Reservation = reservation,
            Message = message,
            HoursUntilSlot = roundedHoursUntilSlot,
            CanStillModify = reservation.Status == "Pending" && exactHoursUntilSlot >= TwelveHours.TotalHours,
        };
    }

    // Flips a slot's IsBooked flag; used whenever a reservation locks, frees, or moves slots.
    private async Task SetSlotBookedAsync(string slotId, bool isBooked)
    {
        await _db.Slots.UpdateOneAsync(s => s.Id == slotId, Builders<EnergyBookingSlot>.Update.Set(s => s.IsBooked, isBooked));
    }

    // Generates a 64-character hex QR token from two concatenated GUIDs.
    internal static string GenerateQrToken()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    // Maps an EnergyReservation document to its public response shape, omitting the QR token.
    private static ReservationResponse ToResponse(EnergyReservation reservation)
    {
        return new ReservationResponse
        {
            Id = reservation.Id,
            ProsumerNic = reservation.ProsumerNic,
            ProsumerName = reservation.ProsumerName,
            StationId = reservation.StationId,
            StationName = reservation.StationName,
            SlotId = reservation.SlotId,
            SlotStartTime = reservation.SlotStartTime,
            SlotEndTime = reservation.SlotEndTime,
            CapacityKw = reservation.CapacityKw,
            Status = reservation.Status,
            QrGeneratedAt = reservation.QrGeneratedAt,
            CreatedAt = reservation.CreatedAt,
            CreatedBy = reservation.CreatedBy,
            UpdatedAt = reservation.UpdatedAt,
            ApprovedAt = reservation.ApprovedAt,
            ApprovedBy = reservation.ApprovedBy,
            CompletedAt = reservation.CompletedAt,
            CompletedBy = reservation.CompletedBy,
            CancelledAt = reservation.CancelledAt,
            CancelledBy = reservation.CancelledBy,
            CancellationReason = reservation.CancellationReason,
        };
    }
}
