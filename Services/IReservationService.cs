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
    Task<List<ReservationResponse>> GetAllAsync(string? status, string? stationId, string? prosumerNic);
    Task<ReservationResponse?> GetByIdAsync(string id);
    Task<ReservationResponse> CreateAsync(CreateReservationRequest request, string createdBy);
    Task<ReservationResponse?> UpdateAsync(string id, UpdateReservationRequest request, string updatedBy);
    Task<ReservationResponse?> CancelAsync(string id, CancelReservationRequest request, string cancelledBy, bool allowOverride);
    Task<ReservationResponse?> ApproveAsync(string id, string approvedBy);
    Task<ReservationResponse?> CompleteAsync(string id, string completedBy);
    Task<(bool Valid, ReservationResponse? Reservation, string? Error)> VerifyQrAsync(VerifyQrRequest request);
    Task<string?> GetQrTokenAsync(string reservationId);
    Task<bool> HasActiveReservationsAsync(string stationId);
    Task<bool> SlotHasActiveReservationAsync(string slotId);
    Task<PagedResult<ReservationResponse>> SearchAsync(ReservationSearchRequest request);
}
