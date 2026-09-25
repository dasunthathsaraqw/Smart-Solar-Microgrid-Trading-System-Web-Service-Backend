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

    // Set only for prosumer login accounts, where it links to the Prosumers profile and is copied into the JWT "nic" claim. Null for staff.
    [BsonElement("nic")]
    public string? Nic { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    // The login identifier. Has a unique index, but the index is case-sensitive; UserService additionally checks case-insensitively.
    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    // BCrypt hash, never the password itself. Excluded from every response model.
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    // Allowed values: "Backoffice", "GridOperator", "Prosumer"
    [BsonElement("role")]
    public string Role { get; set; } = string.Empty;

    // Station a GridOperator is assigned to, which scopes everything they can see or do. Absent (not stored as null) when unassigned or for other roles.
    [BsonElement("stationId")]
    [BsonIgnoreIfNull]
    public string? StationId { get; set; }

    // Login gate: an inactive account is refused at login with 403. Staff start active; prosumer accounts start inactive until approved.
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("createdBy")]
    public string? CreatedBy { get; set; }

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
