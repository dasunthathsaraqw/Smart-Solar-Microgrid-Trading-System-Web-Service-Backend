/**
 * File: LoginResponse.cs
 * Purpose: Response payload returned after a successful login (JWT + user info).
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
