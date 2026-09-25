/**
 * File: BulkCreateSlotRequest.cs
 * Purpose: Request payload for POST /api/slots/bulk — generates multiple fixed-interval slots
 *          across a single day for a station (e.g. every 60 minutes from 06:00 to 20:00).
 * Author: P.D.D.T Hemachandra it23390232
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

    // Times of day (e.g. "06:00:00") for the first slot start and the last slot end, applied to SlotDate. Not [Required], so an omitted value
    // becomes 00:00 and the service then rejects an end that is not after the start.
    public TimeSpan StartTime { get; set; }

    public TimeSpan EndTime { get; set; }

    // 30 to 480 minutes, the same 30-minute to 8-hour bounds SlotService applies to single slots.
    [Range(30, 480, ErrorMessage = "Slot duration must be between 30 minutes and 8 hours.")]
    public int SlotDurationMinutes { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double CapacityPerSlotKw { get; set; }
}
