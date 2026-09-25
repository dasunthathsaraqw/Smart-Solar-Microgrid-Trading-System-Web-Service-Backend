/**
 * File: EnergyBookingSlot.cs
 * Purpose: MongoDB document model representing an available (or booked) time window at a station.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SmartMicrogrid.API.Models;

public class EnergyBookingSlot
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("stationId")]
    public string StationId { get; set; } = string.Empty;

    // Denormalized for display convenience (avoids a join/lookup when listing slots).
    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    // Calendar day the slot belongs to (time part is dropped on creation).
    [BsonElement("slotDate")]
    public DateTime SlotDate { get; set; }

    // Full date-time window of the slot, treated as UTC. Slots at one station may not overlap, but may touch end-to-start.
    [BsonElement("startTime")]
    public DateTime StartTime { get; set; }

    [BsonElement("endTime")]
    public DateTime EndTime { get; set; }

    // Energy capacity in kilowatts offered in this slot; copied into a reservation when it is booked.
    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    // Locking flag flipped by ReservationService: true while a Pending/Approved reservation holds the slot, false again on cancel or completion.
    // A booked slot cannot be edited or deleted.
    [BsonElement("isBooked")]
    public bool IsBooked { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
