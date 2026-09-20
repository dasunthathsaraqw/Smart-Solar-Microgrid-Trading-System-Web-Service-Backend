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
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<User?> GetByIdAsync(string id);
}
