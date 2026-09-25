/**
 * File: RegisterProsumerRequest.cs
 * Purpose: Request payload for mobile prosumer self-registration.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class RegisterProsumerRequest
{
    // Sri Lankan NIC: old format (9 digits + V/X) or new format (12 digits). It becomes the prosumer's permanent identifier.
    [Required]
    [RegularExpression(@"^([0-9]{9}[vVxX]|[0-9]{12})$", ErrorMessage = "Invalid Sri Lankan NIC format.")]
    public string Nic { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    // Doubles as the login email.
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    // Plain text in transit only; the service hashes it before storing. Minimum is 8 here versus 6 in CreateProsumerRequest.
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string Password { get; set; } = string.Empty;

    // Any 10 digits, unlike the Backoffice create/update requests which also require a leading 0.
    [Required]
    [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Contact number must contain exactly 10 digits.")]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Address { get; set; } = string.Empty;

    // Panel capacity in kW; the self-registration range is 0.1 to 1000, narrower than the Backoffice ranges.
    [Range(0.1, 1000)]
    public double PanelCapacityKw { get; set; }
}
