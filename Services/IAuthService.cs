/**
 * File: IAuthService.cs
 * Purpose: Contract for authentication operations (login, user lookup).
 * Author: <Your Name>
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request);
    Task<User?> GetByIdAsync(string id);
}
