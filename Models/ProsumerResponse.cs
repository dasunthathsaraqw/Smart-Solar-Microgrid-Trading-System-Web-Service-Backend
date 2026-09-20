/**
 * File: ProsumerResponse.cs
 * Purpose: Prosumer data returned to clients — mirrors Prosumer but omits the password hash
 *          and adds a computed Status field ("active" | "pending" | "deactivated").
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class ProsumerResponse
{
    public string Id { get; set; } = string.Empty;
    public string Nic { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ContactNumber { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double PanelCapacityKw { get; set; }
    public bool IsActive { get; set; }
    public bool DeactivationRequested { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = string.Empty;
}
