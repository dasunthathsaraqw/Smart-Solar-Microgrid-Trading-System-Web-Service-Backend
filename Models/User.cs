/**
 * File: User.cs
 * Purpose: MongoDB document model representing a system user (Backoffice, GridOperator or Prosumer).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SmartMicrogrid.API.Models;

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("nic")]
    public string? Nic { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    // Allowed values: "Backoffice", "GridOperator", "Prosumer"
    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    [BsonElement("stationId")]
    [BsonIgnoreIfNull]
    public string? StationId { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string? CreatedBy { get; set; }

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
