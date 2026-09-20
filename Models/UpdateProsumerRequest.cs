/**
 * File: UpdateProsumerRequest.cs
 * Purpose: Request payload for PUT /api/prosumers/{nic} — partial update of an existing prosumer.
 *          The NIC itself is never editable since it is the prosumer's primary identifier.
 * Author: <Your Name>
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateProsumerRequest
{
    public string? Name { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [RegularExpression(@"^0[0-9]{9}$", ErrorMessage = "Contact number must be 10 digits starting with 0.")]
    public string? ContactNumber { get; set; }

    public string? Address { get; set; }

    [Range(0.01, 100000, ErrorMessage = "Panel capacity must be greater than 0.")]
    public double? PanelCapacityKw { get; set; }
}
