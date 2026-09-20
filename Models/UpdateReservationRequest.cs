/**
 * File: UpdateReservationRequest.cs
 * Purpose: Request payload for PUT /api/reservations/{id} — moves a Pending reservation to a
 *          different slot at the same station. The 12-hour rule is enforced in ReservationService.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateReservationRequest
{
    [Required]
    public string NewSlotId { get; set; } = string.Empty;
}
