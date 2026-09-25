/**
 * File: CreateUserRequest.cs
 * Purpose: Request payload for POST /api/users — creating a Backoffice or GridOperator account.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateUserRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    // Plain text in transit only; UserService hashes it. Minimum is 6 characters, versus 8 for prosumer self-registration and password changes.
    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    // Only "Backoffice" or "GridOperator" are allowed to be created in this stage.
    [Required]
    [RegularExpression("^(Backoffice|GridOperator)$", ErrorMessage = "Role must be 'Backoffice' or 'GridOperator'.")]
    public string Role { get; set; } = string.Empty;

    // Accepted in the payload but not stored: UserService.CreateAsync does not copy it onto the new account.
    public string? Nic { get; set; }

    // Only used when Role is GridOperator, and must be an existing station's ObjectId. Optional; the operator can be assigned later.
    public string? StationId { get; set; }
}
