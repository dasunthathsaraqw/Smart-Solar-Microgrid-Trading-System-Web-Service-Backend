/**
 * File: SolarStationInfo.cs
 * Purpose: MongoDB document model representing a microgrid solar station (node) managed by the Backoffice.
 * Author: <Your Name>
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

    [BsonElement("stationName")]
    public string StationName { get; set; } = string.Empty;

    [BsonElement("latitude")]
    public double Latitude { get; set; }

    [BsonElement("longitude")]
    public double Longitude { get; set; }

    [BsonElement("capacityKw")]
    public double CapacityKw { get; set; }

    [BsonElement("availableSlots")]
    public int AvailableSlots { get; set; }

    [BsonElement("schedule")]
    public string Schedule { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
