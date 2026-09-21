/**
 * File: CreateOwnReservationRequest.cs
 * Purpose: Prosumer booking input that does not require a client-supplied NIC.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateOwnReservationRequest
{
    public string? ProsumerNic { get; set; }

    [Required]
    public string StationId { get; set; } = string.Empty;

    [Required]
    public string SlotId { get; set; } = string.Empty;
}
