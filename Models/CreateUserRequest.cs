/**
 * File: CreateUserRequest.cs
 * Purpose: Request payload for POST /api/users — creating a Backoffice or GridOperator account.
 * Author: <Your Name>
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateUserRequest
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    // Only "Backoffice" or "GridOperator" are allowed to be created in this stage.
    [Required]
    [RegularExpression("^(Backoffice|GridOperator)$", ErrorMessage = "Role must be 'Backoffice' or 'GridOperator'.")]
    public string Role { get; set; } = string.Empty;

    public string? Nic { get; set; }
}
