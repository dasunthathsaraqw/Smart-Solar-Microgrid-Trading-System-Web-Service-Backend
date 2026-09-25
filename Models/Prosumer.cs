/**
 * File: Prosumer.cs
 * Purpose: MongoDB document model representing a solar prosumer managed by the Backoffice.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SmartMicrogrid.API.Models;

public class Prosumer
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // The NIC is the prosumer's primary business identifier and must be unique.
    [BsonElement("nic")]
    public string Nic { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    // Also the login email for self-registered prosumers, so it is kept in sync with the Users document.
    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("contactNumber")]
    public string ContactNumber { get; set; } = string.Empty;

    [BsonElement("address")]
    public string Address { get; set; } = string.Empty;

    // Installed solar panel capacity in kilowatts.
    [BsonElement("panelCapacityKw")]
    public double PanelCapacityKw { get; set; }

    // Hashed, never plain text; never returned to clients (see ProsumerResponse). Mirrors the hash on the matching Users document.
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    // New prosumers start inactive and require Backoffice approval.
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = false;

    // True once a Backoffice user has deactivated a previously active prosumer;
    // distinguishes "deactivated" from "pending" when IsActive is false.
    [BsonElement("deactivationRequested")]
    public bool DeactivationRequested { get; set; } = false;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // "self-registration" for mobile sign-ups, otherwise the email of the Backoffice user who created the profile.
    [BsonElement("createdBy")]
    public string CreatedBy { get; set; } = string.Empty;

    // Null until the first edit or lifecycle change (approval, deactivation request, password change).
    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}
