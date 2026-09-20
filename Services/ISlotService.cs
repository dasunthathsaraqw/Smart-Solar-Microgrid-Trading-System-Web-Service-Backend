/**
 * File: ISlotService.cs
 * Purpose: Contract for slot availability, CRUD, bulk generation and timing rule enforcement.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface ISlotService
{
    // Returns slots optionally filtered by station and derived status.
    Task<List<SlotResponse>> GetAllAsync(string? stationId, string? status);

    // Finds one slot by its MongoDB identifier.
    Task<SlotResponse?> GetByIdAsync(string id);

    // Returns every slot belonging to a station for management clients.
    Task<List<SlotResponse>> GetByStationAsync(string stationId);

    // Returns the active station's unbooked slots starting within the next seven days.
    Task<List<SlotResponse>> GetAvailableByStationAsync(string stationId);

    // Creates a single validated slot.
    Task<SlotResponse> CreateAsync(CreateSlotRequest request, string createdBy);

    // Generates validated fixed-interval slots for a day.
    Task<List<SlotResponse>> BulkCreateAsync(BulkCreateSlotRequest request, string createdBy);

    // Updates an existing unbooked slot.
    Task<SlotResponse?> UpdateAsync(string id, UpdateSlotRequest request);

    // Deletes an existing unbooked slot.
    Task<bool> DeleteAsync(string id);

    // Reports whether a proposed time window overlaps another station slot.
    Task<bool> HasOverlapAsync(string stationId, DateTime startTime, DateTime endTime, string? excludeId = null);
}
