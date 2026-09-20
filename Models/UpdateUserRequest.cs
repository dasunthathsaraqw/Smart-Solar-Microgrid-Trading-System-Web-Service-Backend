/**
 * File: UpdateUserRequest.cs
 * Purpose: Request payload for PUT /api/users/{id} — partial update of an existing user.
 * Author: <Your Name>
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateUserRequest
{
    public string? Name { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [MinLength(6)]
    public string? Password { get; set; }

    public bool? IsActive { get; set; }
}
