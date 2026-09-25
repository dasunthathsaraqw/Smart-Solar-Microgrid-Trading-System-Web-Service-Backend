/**
 * File: LoginRequest.cs
 * Purpose: Request payload for POST /api/auth/login.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class LoginRequest
{
    // Matched exactly as typed (case-sensitive); the same email casing used at registration must be used to log in.
    /// <summary>Registered account email address.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Account password.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}
