/**
 * File: IStationService.cs
 * Purpose: Contract for station CRUD, nearby search and reservation-aware lifecycle operations.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IStationService
{
    // Returns stations optionally filtered by lifecycle status.
    Task<List<StationResponse>> GetAllAsync(string? status);

    // Finds one station by its MongoDB identifier.
    Task<StationResponse?> GetByIdAsync(string id);

    // Finds active stations within a validated geographic radius.
    Task<List<NearbyStationResponse>> GetNearbyAsync(
        double latitude,
        double longitude,
        double radiusKm,
        int limit);

    // Creates a Backoffice-managed station.
    Task<StationResponse> CreateAsync(CreateStationRequest request, string createdBy);

    // Updates the editable fields of an existing station.
    Task<StationResponse?> UpdateAsync(string id, UpdateStationRequest request);

    // Deactivates a station when approved reservations do not block it.
    Task<(bool Success, string? Error)> DeactivateAsync(string id);

    // Reactivates an existing station.
    Task<bool> ReactivateAsync(string id);

    // Reports whether a case-insensitive station name is already in use.
    Task<bool> NameExistsAsync(string stationName, string? excludeId = null);

    // Reports whether approved reservations prevent station deactivation.
    Task<bool> HasActiveReservationsAsync(string stationId);
}
