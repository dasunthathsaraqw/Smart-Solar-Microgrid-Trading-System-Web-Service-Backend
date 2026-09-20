/**
 * File: UpdateUserRequest.cs
 * Purpose: Request payload for PUT /api/users/{id} — partial update of an existing user.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateUserRequest
{
    [StringLength(100, MinimumLength = 2)]
    public string? Name { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [MinLength(6)]
    public string? Password { get; set; }

    // Only "Backoffice" or "GridOperator" are allowed for this stage.
    [RegularExpression("^(Backoffice|GridOperator)$", ErrorMessage = "Role must be 'Backoffice' or 'GridOperator'.")]
    public string? Role { get; set; }

    public bool? IsActive { get; set; }
}
