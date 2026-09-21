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
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}
