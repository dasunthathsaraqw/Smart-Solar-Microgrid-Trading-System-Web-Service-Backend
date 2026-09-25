/**
 * File: CreateStationRequest.cs
 * Purpose: Request payload for POST /api/stations — registers a new microgrid station.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateStationRequest
{
    // Must be unique ignoring case; the service returns a conflict if the name is taken.
    [Required]
    [StringLength(100, MinimumLength = 3)]
    public string StationName { get; set; } = string.Empty;

    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double Longitude { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double CapacityKw { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Available slots must be 0 or more.")]
    public int AvailableSlots { get; set; }

    // Operating hours text such as "06:00-20:00 Mon-Sun"; slots are later validated against it by ScheduleValidator.
    [Required]
    public string Schedule { get; set; } = string.Empty;
}
