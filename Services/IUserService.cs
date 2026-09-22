/**
 * File: IUserService.cs
 * Purpose: Contract for Backoffice/GridOperator user CRUD and lifecycle operations, including
 *          the self-deactivation and last-active-Backoffice safety rules.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IUserService
{
    Task<List<UserResponse>> GetAllAsync(string? role, string? status);
    Task<UserResponse?> GetByIdAsync(string id);
    Task<UserResponse?> GetByEmailAsync(string email);
    Task<UserResponse> CreateAsync(CreateUserRequest request, string createdByEmail);
    Task<UserResponse?> UpdateAsync(string id, UpdateUserRequest request, string updatedByEmail);
    Task<(bool Success, string? Error)> DeactivateAsync(string id, string requestingUserId);
    Task<bool> ReactivateAsync(string id);
    Task<bool> EmailExistsAsync(string email, string? excludeId = null);
    Task<int> CountActiveBackofficesAsync();

    // Resolves an operator's persisted station and rejects unassigned or foreign requested scopes.
    Task<(bool UserExists, string? StationId, string? Error)> ResolveOperatorStationAsync(
        string operatorId,
        string? requestedStationId);
}
