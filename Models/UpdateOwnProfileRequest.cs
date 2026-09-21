/**
 * File: UpdateOwnProfileRequest.cs
 * Purpose: Request payload for a prosumer to update only their editable profile fields.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateOwnProfileRequest
{
    [StringLength(100)]
    public string? Name { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Contact number must contain exactly 10 digits.")]
    public string? ContactNumber { get; set; }

    [StringLength(200)]
    public string? Address { get; set; }

    [Range(0.1, 1000)]
    public double? PanelCapacityKw { get; set; }
}
