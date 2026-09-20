/**
 * File: IProsumerService.cs
 * Purpose: Contract for prosumer CRUD and lifecycle operations (approve/deactivate/reactivate).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IProsumerService
{
    Task<List<ProsumerResponse>> GetAllAsync(string? status);
    Task<ProsumerResponse?> GetByNicAsync(string nic);
    Task<ProsumerResponse> CreateAsync(CreateProsumerRequest request, string createdBy);
    Task<ProsumerResponse?> UpdateAsync(string nic, UpdateProsumerRequest request);
    Task<bool> DeactivateAsync(string nic);
    Task<bool> ReactivateAsync(string nic);
    Task<bool> NicExistsAsync(string nic);
    Task<bool> EmailExistsAsync(string email);
}
