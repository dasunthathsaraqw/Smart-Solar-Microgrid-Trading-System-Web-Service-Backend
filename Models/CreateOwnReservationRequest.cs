/**
 * File: CreateOwnReservationRequest.cs
 * Purpose: Prosumer booking input that does not require a client-supplied NIC.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateOwnReservationRequest
{
    // Optional and ignored: the server replaces it with the NIC from the signed token (see CreateForProsumerAsync).
    public string? ProsumerNic { get; set; }

    [Required]
    public string StationId { get; set; } = string.Empty;

    [Required]
    public string SlotId { get; set; } = string.Empty;
}
