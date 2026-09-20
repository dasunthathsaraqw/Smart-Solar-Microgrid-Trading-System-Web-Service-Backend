/**
 * File: EnergyBookingSlot.cs
 * Purpose: MongoDB document model representing an available (or booked) time window at a station.
 * Author: <Your Name>
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

    [BsonElement("slotDate")]
    public DateTime SlotDate { get; set; }

    [BsonElement("startTime")]
    public DateTime StartTime { get; set; }

    [BsonElement("endTime")]
    public DateTime EndTime { get; set; }

    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    [BsonElement("isBooked")]
    public bool IsBooked { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
