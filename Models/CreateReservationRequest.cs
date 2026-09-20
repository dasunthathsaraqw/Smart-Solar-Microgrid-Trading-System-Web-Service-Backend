/**
 * File: CreateReservationRequest.cs
 * Purpose: Request payload for POST /api/reservations — books a slot on behalf of a prosumer.
 *          The 7-day rule, station/prosumer active checks and slot-locking are enforced in
 *          ReservationService, not here.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateReservationRequest
{
    [Required]
    public string ProsumerNic { get; set; } = string.Empty;

    [Required]
    public string StationId { get; set; } = string.Empty;

    [Required]
    public string SlotId { get; set; } = string.Empty;
}
