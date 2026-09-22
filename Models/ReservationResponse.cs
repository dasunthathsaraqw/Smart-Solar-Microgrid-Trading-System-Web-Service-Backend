/**
 * File: ReservationResponse.cs
 * Purpose: Reservation data returned to clients — mirrors EnergyReservation but omits QrToken,
 *          which is only ever returned by the dedicated GET /api/reservations/{id}/qr endpoint.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class ReservationResponse
{
    /// <summary>Reservation ObjectId.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>NIC of the owning Prosumer.</summary>
    public string ProsumerNic { get; set; } = string.Empty;

    /// <summary>Prosumer name captured for the reservation.</summary>
    public string ProsumerName { get; set; } = string.Empty;

    /// <summary>Station ObjectId.</summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>Station name captured for the reservation.</summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>Reserved energy slot ObjectId.</summary>
    public string SlotId { get; set; } = string.Empty;

    /// <summary>UTC slot start time.</summary>
    public DateTime SlotStartTime { get; set; }

    /// <summary>UTC slot end time.</summary>
    public DateTime SlotEndTime { get; set; }

    /// <summary>Energy capacity reserved in kilowatts.</summary>
    public double CapacityKw { get; set; }

    /// <summary>Lifecycle status: Pending, Approved, Completed, or Cancelled.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>UTC time the current QR was generated; null until approval.</summary>
    public DateTime? QrGeneratedAt { get; set; }

    /// <summary>UTC reservation creation time.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Identity recorded as the reservation creator.</summary>
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>UTC time of the most recent lifecycle update, when available.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>UTC approval time; null before approval.</summary>
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Identity that approved the reservation.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>UTC completion time; null until the transfer is completed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Identity that completed the transfer.</summary>
    public string? CompletedBy { get; set; }

    /// <summary>UTC cancellation time; null unless cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>Identity that cancelled the reservation.</summary>
    public string? CancelledBy { get; set; }

    /// <summary>Optional cancellation explanation.</summary>
    public string? CancellationReason { get; set; }
}
