/**
 * File: IJwtService.cs
 * Purpose: Contract for generating signed JWT access tokens for authenticated users.
 * Author: <Your Name>
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IJwtService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
