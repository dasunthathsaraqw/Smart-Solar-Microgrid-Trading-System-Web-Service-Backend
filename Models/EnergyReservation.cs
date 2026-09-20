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

    [BsonElement("slotStartTime")]
    public DateTime SlotStartTime { get; set; }

    [BsonElement("slotEndTime")]
    public DateTime SlotEndTime { get; set; }

    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    // One of: "Pending", "Approved", "Completed", "Cancelled".
    [BsonElement("status")]
    public string Status { get; set; } = "Pending";

    [BsonElement("qrToken")]
    public string? QrToken { get; set; }

    [BsonElement("qrGeneratedAt")]
    public DateTime? QrGeneratedAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

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
