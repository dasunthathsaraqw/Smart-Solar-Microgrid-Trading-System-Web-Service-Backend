/**
 * File: StationResponse.cs
 * Purpose: Station data returned to clients — mirrors SolarStationInfo (no sensitive fields to strip).
 * Author: <Your Name>
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class StationResponse
{
    public string Id { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double CapacityKw { get; set; }
    public int AvailableSlots { get; set; }
    public string Schedule { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}
