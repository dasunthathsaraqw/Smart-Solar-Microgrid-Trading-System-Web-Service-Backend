/**
 * File: RegisterProsumerRequest.cs
 * Purpose: Request payload for mobile prosumer self-registration.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class RegisterProsumerRequest
{
    [Required]
    [RegularExpression(@"^([0-9]{9}[vVxX]|[0-9]{12})$", ErrorMessage = "Invalid Sri Lankan NIC format.")]
    public string Nic { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Contact number must contain exactly 10 digits.")]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    [Range(0.1, 1000)]
    public double PanelCapacityKw { get; set; }
}
