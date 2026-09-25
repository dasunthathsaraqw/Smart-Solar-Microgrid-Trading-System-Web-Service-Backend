/**
 * File: UpdateUserRequest.cs
 * Purpose: Request payload for PUT /api/users/{id} — partial update of an existing user.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SmartMicrogrid.API.Models;

// Every property is optional; only supplied values change. StationId is the exception to "null means unchanged" (see below).
public class UpdateUserRequest
{
    private string? _stationId;

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

    // Needs a "was it sent?" flag because an omitted field and an explicit null both deserialize to null, yet they mean different things:
    // omitted keeps the current station, while an explicit null (or blank) unassigns it. The setter only runs when the JSON contains the property.
    public string? StationId
    {
        get => _stationId;
        set
        {
            _stationId = value;
            StationIdSpecified = true;
        }
    }

    // Set by the StationId setter above; hidden from JSON so clients cannot supply it directly.
    [JsonIgnore]
    public bool StationIdSpecified { get; private set; }
}
