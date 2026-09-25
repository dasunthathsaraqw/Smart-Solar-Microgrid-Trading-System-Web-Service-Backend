/**
 * File: SolarStationInfo.cs
 * Purpose: MongoDB document model representing a microgrid solar station (node) managed by the Backoffice.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SmartMicrogrid.API.Models;

public class SolarStationInfo
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // Unique case-insensitively (enforced by a unique index as well as a check in StationService).
    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    // GPS position in decimal degrees; used by the nearby-station search.
    [BsonElement("latitude")]
    public double Latitude { get; set; }

    [BsonElement("longitude")]
    public double Longitude { get; set; }

    // Station's total capacity in kilowatts.
    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    // Figure entered by Backoffice; not recalculated from the Slots collection.
    [BsonElement("availableSlots")]
    public int AvailableSlots { get; set; }

    // Operating hours as text, e.g. "06:00-20:00 Mon-Sun". Parsed by ScheduleValidator, which checks slots against it in the Asia/Colombo time zone.
    [BsonElement("schedule")]
    public string Schedule { get; set; } = string.Empty;

    // Deactivated stations disappear from mobile views and cannot receive new slots or bookings. New stations start active.
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
