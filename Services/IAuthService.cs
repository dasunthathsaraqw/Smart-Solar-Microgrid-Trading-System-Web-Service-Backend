/**
 * File: IAuthService.cs
 * Purpose: Contract for authentication operations (login, user lookup).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IAuthService
{
    // Validates credentials and distinguishes an inactive account from invalid credentials.
    Task<(LoginResponse? Response, bool AccountInactive)> LoginAsync(LoginRequest request);

    // Finds the credential account represented by a JWT subject identifier.
    Task<User?> GetByIdAsync(string id);
}
