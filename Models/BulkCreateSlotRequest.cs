/**
 * File: BulkCreateSlotRequest.cs
 * Purpose: Request payload for POST /api/slots/bulk — generates multiple fixed-interval slots
 *          across a single day for a station (e.g. every 60 minutes from 06:00 to 20:00).
 * Author: <Your Name>
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class BulkCreateSlotRequest
{
    [Required]
    public string StationId { get; set; } = string.Empty;

    [Required]
    public DateTime SlotDate { get; set; }

    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    [Range(30, 480, ErrorMessage = "Slot duration must be between 30 minutes and 8 hours.")]
    public int SlotDurationMinutes { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double CapacityPerSlotKw { get; set; }
}
