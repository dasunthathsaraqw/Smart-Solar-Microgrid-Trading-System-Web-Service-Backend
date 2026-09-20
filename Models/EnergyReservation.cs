/**
 * File: EnergyReservation.cs
 * Purpose: Minimal placeholder — will be expanded in Stage 5. Only holds the fields needed by
 *          StationService to check the "active reservations" station-deactivation block rule.
 * Author: <Your Name>
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

    [BsonElement("stationId")]
    public string StationId { get; set; } = string.Empty;

    // Expected value for an active reservation is the exact string "Approved".
    [BsonElement("status")]
    public string Status { get; set; } = string.Empty;
}
