/**
 * File: LoginResponse.cs
 * Purpose: Response payload returned after a successful login (JWT + user info).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class LoginResponse
{
    /// <summary>Bearer JWT used in the Authorization header.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Current display name from the authenticated account.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current email address from the authenticated account.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Authorization role: Backoffice, GridOperator, or Prosumer.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Persisted GridOperator station assignment; null for unassigned operators and other roles.</summary>
    public string? StationId { get; set; }

    /// <summary>UTC instant at which the JWT expires.</summary>
    public DateTime ExpiresAt { get; set; }
}
