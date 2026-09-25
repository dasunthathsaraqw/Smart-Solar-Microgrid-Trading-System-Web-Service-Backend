/**
 * File: EnergyReservation.cs
 * Purpose: MongoDB document model representing a prosumer's booking of an energy slot,
 *          through its full lifecycle: Pending -> Approved -> Completed, or Cancelled.
 *          Replaces the minimal Stage 3 placeholder; StationId and Status keep the same
 *          field names so StationService's deactivation-block query still works unchanged.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SmartMicrogrid.API.Models;

public class EnergyReservation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // Links the booking to its owner; every "my reservations" query and ownership check filters on this.
    [BsonElement("prosumerNic")]
    public string ProsumerNic { get; set; } = string.Empty;

    // Denormalized snapshots for display convenience.
    [BsonElement("prosumerName")]
    public string ProsumerName { get; set; } = string.Empty;

    [BsonElement("stationId")]
    public string StationId { get; set; } = string.Empty;

    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    [BsonElement("slotId")]
    public string SlotId { get; set; } = string.Empty;

    // Copied from the slot at booking time (and refreshed when the reservation is moved). All booking rules are evaluated against this value in UTC.
    [BsonElement("slotStartTime")]
    public DateTime SlotStartTime { get; set; }

    [BsonElement("slotEndTime")]
    public DateTime SlotEndTime { get; set; }

    // Energy capacity in kilowatts taken from the slot.
    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    // One of: "Pending", "Approved", "Completed", "Cancelled".
    [BsonElement("status")]
    public string Status { get; set; } = "Pending";

    // Secret used to verify the transfer at the station. Set on approval, cleared on cancel or completion, and never included in ReservationResponse.
    [BsonElement("qrToken")]
    public string? QrToken { get; set; }

    // When the current QR was issued; null before approval. Kept after completion, cleared on cancel.
    [BsonElement("qrGeneratedAt")]
    public DateTime? QrGeneratedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Email of the Backoffice/Operator who booked, or the prosumer's NIC for self-service bookings.
    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    // Null until the first change after creation.
    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    // The approved/completed/cancelled audit pairs below stay null until that transition happens; only one of completed or cancelled is ever set.
    [BsonElement("approvedAt")]
    public DateTime? ApprovedAt { get; set; }

    [BsonElement("approvedBy")]
    public string? ApprovedBy { get; set; }

    [BsonElement("completedAt")]
    public DateTime? CompletedAt { get; set; }

    [BsonElement("completedBy")]
    public string? CompletedBy { get; set; }

    [BsonElement("cancelledAt")]
    public DateTime? CancelledAt { get; set; }

    [BsonElement("cancelledBy")]
    public string? CancelledBy { get; set; }

    [BsonElement("cancellationReason")]
    public string? CancellationReason { get; set; }
}
