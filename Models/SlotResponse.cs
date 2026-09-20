/**
 * File: SlotResponse.cs
 * Purpose: Slot data returned to clients — mirrors EnergyBookingSlot (no sensitive fields to strip).
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class SlotResponse
{
    public string Id { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public DateTime SlotDate { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double CapacityKw { get; set; }
    public bool IsBooked { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}
