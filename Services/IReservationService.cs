/**
 * File: IReservationService.cs
 * Purpose: Contract for the reservation lifecycle (create, update, cancel, approve, complete)
 *          and QR issuance/verification, including the 7-day and 12-hour business rules.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IReservationService
{
    // Lists reservations using management filters.
    Task<List<ReservationResponse>> GetAllAsync(string? status, string? stationId, string? prosumerNic);

    // Finds a reservation by its identifier.
    Task<ReservationResponse?> GetByIdAsync(string id);

    // Lists only reservations owned by the authenticated prosumer.
    Task<List<ReservationResponse>> GetByProsumerAsync(string prosumerNic, string? status);

    // Checks ownership without revealing whether another prosumer's reservation exists.
    Task<bool> IsOwnedByAsync(string reservationId, string prosumerNic);

    // Creates a reservation through the existing booking rules for a management caller.
    Task<ReservationResponse> CreateAsync(CreateReservationRequest request, string createdBy);

    // Replaces the supplied NIC with the authenticated NIC before invoking existing booking rules.
    Task<ReservationResponse> CreateForProsumerAsync(CreateReservationRequest request, string prosumerNic);

    // Updates a pending reservation using the existing notice and slot rules.
    Task<ReservationResponse?> UpdateAsync(string id, UpdateReservationRequest request, string updatedBy);

    // Cancels a reservation using the existing notice and override rules.
    Task<ReservationResponse?> CancelAsync(string id, CancelReservationRequest request, string cancelledBy, bool allowOverride);

    // Approves a pending reservation and issues its QR token.
    Task<ReservationResponse?> ApproveAsync(string id, string approvedBy);

    // Completes an approved reservation and frees its slot.
    Task<ReservationResponse?> CompleteAsync(string id, string completedBy);

    // Verifies a QR token at the point of energy transfer.
    Task<(bool Valid, ReservationResponse? Reservation, string? Error)> VerifyQrAsync(VerifyQrRequest request);

    // Returns the QR token only for an approved reservation.
    Task<string?> GetQrTokenAsync(string reservationId);

    // Reports whether a station has approved reservations.
    Task<bool> HasActiveReservationsAsync(string stationId);

    // Reports whether a slot has a live reservation.
    Task<bool> SlotHasActiveReservationAsync(string slotId);

    // Searches reservations with management filters and pagination.
    Task<PagedResult<ReservationResponse>> SearchAsync(ReservationSearchRequest request);

    // Forces the authenticated prosumer's NIC into the existing paged search.
    Task<PagedResult<ReservationResponse>> SearchForProsumerAsync(string prosumerNic, ReservationSearchRequest request);

    // Builds server-computed confirmation metadata for a completed prosumer action.
    ReservationActionResponse CreateActionResponse(ReservationResponse reservation, string action);
}
