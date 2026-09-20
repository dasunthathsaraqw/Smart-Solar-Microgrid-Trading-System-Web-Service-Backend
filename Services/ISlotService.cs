/**
 * File: ISlotService.cs
 * Purpose: Contract for energy booking slot CRUD, bulk generation, and overlap/timing rule enforcement.
 * Author: <Your Name>
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface ISlotService
{
    Task<List<SlotResponse>> GetAllAsync(string? stationId, string? status);
    Task<SlotResponse?> GetByIdAsync(string id);
    Task<List<SlotResponse>> GetByStationAsync(string stationId);
    Task<SlotResponse> CreateAsync(CreateSlotRequest request, string createdBy);
    Task<List<SlotResponse>> BulkCreateAsync(BulkCreateSlotRequest request, string createdBy);
    Task<SlotResponse?> UpdateAsync(string id, UpdateSlotRequest request);
    Task<bool> DeleteAsync(string id);
    Task<bool> HasOverlapAsync(string stationId, DateTime startTime, DateTime endTime, string? excludeId = null);
}
