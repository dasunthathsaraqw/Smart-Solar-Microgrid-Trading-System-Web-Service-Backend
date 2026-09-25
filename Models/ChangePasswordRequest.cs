/**
 * File: ChangePasswordRequest.cs
 * Purpose: Request payload for an authenticated prosumer password change.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class ChangePasswordRequest
{
    // Verified against the stored hash before anything changes, so a stolen session alone cannot set a new password.
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    // Same 8-character minimum as self-registration.
    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}
