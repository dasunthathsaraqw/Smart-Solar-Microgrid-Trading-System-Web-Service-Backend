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
    // Returns all prosumers, optionally filtered by their lifecycle status.
    Task<List<ProsumerResponse>> GetAllAsync(string? status);

    // Finds a prosumer by their immutable NIC business identifier.
    Task<ProsumerResponse?> GetByNicAsync(string nic);

    // Creates synchronized inactive profile and credential documents for self-registration.
    Task<ProsumerResponse> RegisterAsync(RegisterProsumerRequest request);

    // Creates a Backoffice-managed prosumer profile.
    Task<ProsumerResponse> CreateAsync(CreateProsumerRequest request, string createdBy);

    // Updates the Backoffice-editable fields for a specified prosumer. Returns null when the NIC is unknown.
    Task<ProsumerResponse?> UpdateAsync(string nic, UpdateProsumerRequest request);

    // Updates the authenticated prosumer's permitted profile fields.
    Task<ProsumerResponse?> UpdateOwnProfileAsync(string nic, UpdateOwnProfileRequest request);

    // Changes both stored password hashes after current-password verification.
    Task<bool> ChangePasswordAsync(string nic, ChangePasswordRequest request);

    // Requests deactivation when no open reservation blocks it.
    Task<(bool Success, string? Error)> RequestDeactivationAsync(string nic);

    // Returns active prosumers waiting for Backoffice deactivation approval.
    Task<List<ProsumerResponse>> GetPendingDeactivationsAsync();

    // Deactivates the profile and matching credential account.
    Task<bool> DeactivateAsync(string nic);

    // Approves or reactivates the profile and matching credential account.
    Task<bool> ReactivateAsync(string nic);

    // Reports whether the NIC is already present in the profile collection.
    Task<bool> NicExistsAsync(string nic);

    // Reports whether the email is already present in the profile collection.
    Task<bool> EmailExistsAsync(string email);
}
