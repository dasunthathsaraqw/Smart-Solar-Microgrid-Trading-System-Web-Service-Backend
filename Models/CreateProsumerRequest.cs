/**
 * File: CreateProsumerRequest.cs
 * Purpose: Request payload for POST /api/prosumers — Backoffice-created prosumer registration.
 * Author: <Your Name>
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateProsumerRequest
{
    // Sri Lankan NIC: old format (9 digits + V/X) or new format (12 digits).
    [Required]
    [RegularExpression(@"^([0-9]{9}[VvXx]|[0-9]{12})$", ErrorMessage = "Invalid NIC format.")]
    public string Nic { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^0[0-9]{9}$", ErrorMessage = "Contact number must be 10 digits starting with 0.")]
    public string ContactNumber { get; set; } = string.Empty;

    [Required]
    public string Address { get; set; } = string.Empty;

    [Range(0.01, 100000, ErrorMessage = "Panel capacity must be greater than 0.")]
    public double PanelCapacityKw { get; set; }

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
}
