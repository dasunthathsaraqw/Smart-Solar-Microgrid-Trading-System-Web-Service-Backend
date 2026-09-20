/**
 * File: IStationService.cs
 * Purpose: Contract for microgrid station CRUD and lifecycle operations, including the
 *          reservation-aware deactivation block rule.
 * Author: <Your Name>
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IStationService
{
    Task<List<StationResponse>> GetAllAsync(string? status);
    Task<StationResponse?> GetByIdAsync(string id);
    Task<StationResponse> CreateAsync(CreateStationRequest request, string createdBy);
    Task<StationResponse?> UpdateAsync(string id, UpdateStationRequest request);
    Task<(bool Success, string? Error)> DeactivateAsync(string id);
    Task<bool> ReactivateAsync(string id);
    Task<bool> NameExistsAsync(string stationName, string? excludeId = null);
    Task<bool> HasActiveReservationsAsync(string stationId);
}
