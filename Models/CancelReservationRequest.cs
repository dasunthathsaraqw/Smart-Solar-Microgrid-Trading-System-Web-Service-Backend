/**
 * File: CancelReservationRequest.cs
 * Purpose: Request payload for PUT /api/reservations/{id}/cancel — optional cancellation reason.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CancelReservationRequest
{
    // Free-text explanation stored as CancellationReason; may be omitted.
    [StringLength(200)]
    public string? Reason { get; set; }
}
