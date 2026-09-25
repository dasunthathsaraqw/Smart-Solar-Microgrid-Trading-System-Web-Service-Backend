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
    // Business-rule constants: how far ahead a slot may be booked, the minimum notice to update/cancel, and the QR scan tolerance either side of slot start.
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
        // Each supplied criterion narrows the result (AND); empty ones are skipped so no argument means "everything".
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
        // A malformed id is treated as "not owned", the same result as an unknown id or someone else's reservation.
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
        // Overwrite whatever NIC the client sent so a prosumer can never book on someone else's behalf; the NIC also becomes CreatedBy.
        request.ProsumerNic = prosumerNic;
        return CreateAsync(request, prosumerNic);
    }

    // Books a slot for a prosumer after validating the prosumer, station, slot and 7-day window.
    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, string createdBy)
    {
        // Pending and deactivated prosumers cannot book: only an active (Backoffice-approved) prosumer passes.
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

        // The only failure mapped to 409 (ReservationConflictException); every other rule below is a 400.
        if (slot.IsBooked)
        {
            throw new ReservationConflictException("Slot is already booked");
        }

        if (slot.StationId != request.StationId)
        {
            throw new InvalidOperationException("Slot does not belong to this station");
        }

        // All times are compared in UTC so the rules do not depend on server or client time zones.
        var now = DateTime.UtcNow;
        if (slot.StartTime <= now)
        {
            throw new InvalidOperationException("Cannot book a past slot");
        }

        if (!IsWithinBookingWindow(slot.StartTime, now))
        {
            throw new InvalidOperationException("Reservations must be within 7 days");
        }

        // Only live (Pending/Approved) reservations count, so a prosumer may rebook a slot after cancelling.
        var alreadyReserved = await _db.Reservations.Find(r =>
            r.SlotId == request.SlotId &&
            r.ProsumerNic == request.ProsumerNic &&
            (r.Status == "Pending" || r.Status == "Approved")
        ).AnyAsync();

        if (alreadyReserved)
        {
            throw new InvalidOperationException("Prosumer already has a reservation for this slot");
        }

        // Prosumer, station and slot details are copied in as a snapshot, so history stays readable if those records change later.
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

        // The IsBooked check above and this lock are separate operations (check-then-set), not one atomic step.
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

        // Approved reservations already have a QR issued, so they cannot be moved; they must be cancelled and rebooked.
        if (reservation.Status != "Pending")
        {
            throw new InvalidOperationException("Only pending reservations can be updated");
        }

        // The notice period is measured against the CURRENT slot; the new slot has to pass the booking checks below.
        var now = DateTime.UtcNow;
        if (!HasTwelveHoursNotice(reservation.SlotStartTime, now))
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

        if (!IsWithinBookingWindow(newSlot.StartTime, now))
        {
            throw new InvalidOperationException("Reservations must be within 7 days");
        }

        // Release the old slot and lock the new one before rewriting the reservation; these are separate writes, not a transaction.
        await SetSlotBookedAsync(reservation.SlotId, false);
        await SetSlotBookedAsync(newSlot.Id, true);

        // Station is never changed, only the slot and the slot-derived times/capacity. QR fields are cleared defensively (a Pending reservation should not hold one).
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

        // allowOverride is decided by the controller (true only for Backoffice); the service just applies it.
        var now = DateTime.UtcNow;
        if (!allowOverride && !HasTwelveHoursNotice(reservation.SlotStartTime, now))
        {
            throw new InvalidOperationException("Cancellations require at least 12 hours' notice");
        }

        // Free the slot so another prosumer can book it.
        await SetSlotBookedAsync(reservation.SlotId, false);

        // Clearing the QR token invalidates any QR already shown to the prosumer, so a cancelled booking cannot be scanned.
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

        // The QR token is issued only here, so no reservation can have a QR before it is Approved.
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

        // Administrative completion path (Backoffice only, see the controller). Unlike ScanAndCompleteAsync it reads the status first and then writes,
        // so it does not guard against a concurrent completion. QrGeneratedAt is kept as a record of when the QR was issued; only the token is cleared.
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
        // Lookup is by token alone. Tokens are cleared on cancel/complete, so a used or cancelled QR is "not recognized" here.
        var reservation = await _db.Reservations.Find(r => r.QrToken == request.QrToken).FirstOrDefaultAsync();
        if (reservation is null)
        {
            return (false, null, "QR code not recognized");
        }

        if (reservation.Status != "Approved")
        {
            return (false, null, "QR is no longer valid");
        }

        // Stops a QR issued for one station being accepted at another.
        if (reservation.StationId != request.StationId)
        {
            return (false, null, "QR does not belong to this station");
        }

        // Duration() makes the difference absolute, so scanning up to 24 hours before OR after slot start is accepted.
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
        // Read-only verification first, so all QR rules live in one place; the write below only has to win the race.
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
        // A null result means the filter no longer matched, i.e. another scan changed the reservation between verification and this write.
        if (completed is null)
        {
            return (false, null, "This reservation has already been completed");
        }

        // The slot is freed only by the caller that won the swap, so it is released exactly once.
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
        // Only Approved counts here (Pending does not block deactivation), unlike SlotHasActiveReservationAsync which also counts Pending.
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

        // Out-of-range paging is corrected here rather than rejected; the request model's [Range] attributes already reject bad values at the API edge.
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var page = Math.Max(request.Page, 1);

        var filters = new List<FilterDefinition<EnergyReservation>>();

        if (!string.IsNullOrWhiteSpace(request.ProsumerNic))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(r => r.ProsumerNic, request.ProsumerNic));
        }

        // Regex.Escape treats the search text literally, so characters like "." or "(" cannot alter the query.
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

        // The date range applies to the slot's start time (when the transfer is scheduled), not to when the booking was made.
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

        // An unknown or missing SortBy falls back to slot start time (the "date" sort) instead of failing.
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

        // Count and page are two separate queries, so the total can drift slightly if data changes between them.
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

    // Returns only Completed reservations for the operator transaction history, newest completion first.
    public async Task<PagedResult<ReservationResponse>> GetOperatorTransactionHistoryAsync(
        string? stationId,
        DateTime? dateFrom,
        DateTime? dateTo,
        int page,
        int pageSize)
    {
        // Unlike SearchAsync, invalid paging is rejected with an error here instead of being clamped.
        if (page < 1)
        {
            throw new InvalidOperationException("Page must be 1 or greater");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            throw new InvalidOperationException("PageSize must be between 1 and 100");
        }

        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
        {
            throw new InvalidOperationException("DateFrom must not be after DateTo");
        }

        // Malformed and unknown station ids both report "Station not found"; the ObjectId check comes first so a bad id never reaches the query.
        if (!string.IsNullOrWhiteSpace(stationId) &&
            (!ObjectId.TryParse(stationId, out _) ||
             !await _db.Stations.Find(station => station.Id == stationId).AnyAsync()))
        {
            throw new InvalidOperationException("Station not found");
        }

        var filters = new List<FilterDefinition<EnergyReservation>>
        {
            Builders<EnergyReservation>.Filter.Eq(reservation => reservation.Status, "Completed"),
        };

        if (!string.IsNullOrWhiteSpace(stationId))
        {
            filters.Add(Builders<EnergyReservation>.Filter.Eq(reservation => reservation.StationId, stationId));
        }

        // Here the date range applies to CompletedAt (when the transfer happened), unlike SearchAsync which uses the slot start time.
        if (dateFrom.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Gte(reservation => reservation.CompletedAt, dateFrom.Value));
        }

        if (dateTo.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Lte(reservation => reservation.CompletedAt, dateTo.Value));
        }

        var filter = Builders<EnergyReservation>.Filter.And(filters);
        var totalCount = (int)await _db.Reservations.CountDocumentsAsync(filter);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        var items = await _db.Reservations.Find(filter)
            .SortByDescending(reservation => reservation.CompletedAt)
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
        // The NIC pins results to the caller regardless of what the client sent; the name filter is a management-side criterion and is dropped.
        request.ProsumerNic = prosumerNic;
        request.ProsumerName = null;
        return SearchAsync(request);
    }

    // Computes confirmation text and modifiability from the actual slot time and final status.
    public ReservationActionResponse CreateActionResponse(ReservationResponse reservation, string action)
    {
        // The confirmation text is built here so the mobile app only displays it and holds no wording or rules of its own.
        var message = action switch
        {
            "Created" => "Reservation created successfully.",
            "Updated" => "Reservation updated successfully.",
            "Cancelled" => "Reservation cancelled successfully.",
            _ => throw new InvalidOperationException("Unsupported reservation action."),
        };

        // CanStillModify uses the exact hours; only the displayed HoursUntilSlot is rounded (to 1 decimal, halves away from zero).
        var now = DateTime.UtcNow;
        var exactHoursUntilSlot = HoursUntilSlot(reservation.SlotStartTime, now);
        var roundedHoursUntilSlot = Math.Round(exactHoursUntilSlot, 1, MidpointRounding.AwayFromZero);

        return new ReservationActionResponse
        {
            Action = action,
            Reservation = reservation,
            Message = message,
            HoursUntilSlot = roundedHoursUntilSlot,
            CanStillModify = CanStillModify(reservation, now),
        };
    }

    // Accepts future bookings through the exact seven-day boundary.
    internal static bool IsWithinBookingWindow(DateTime slotStart, DateTime now)
    {
        // Both ends are enforced: strictly in the future, and no more than 7 days out (exactly 7 days is still allowed).
        return slotStart > now && slotStart - now <= SevenDays;
    }

    // Accepts modifications with at least twelve hours of notice.
    internal static bool HasTwelveHoursNotice(DateTime slotStart, DateTime now)
    {
        // Inclusive boundary: exactly 12 hours of notice is enough. A slot already in the past gives a negative span, so it fails.
        return slotStart - now >= TwelveHours;
    }

    // Computes the unrounded number of hours until a slot for confirmation views.
    internal static double HoursUntilSlot(DateTime slotStart, DateTime now)
    {
        return (slotStart - now).TotalHours;
    }

    // Allows only pending reservations with at least twelve hours left to be modified.
    internal static bool CanStillModify(ReservationResponse reservation, DateTime now)
    {
        // Mirrors UpdateAsync's rules so the app's "can still modify" flag matches what the API would accept. Approved bookings are not editable.
        return reservation.Status == "Pending" && HasTwelveHoursNotice(reservation.SlotStartTime, now);
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
        // QrToken is intentionally not copied: it is only exposed through the dedicated QR endpoints, with their ownership and role checks.
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
