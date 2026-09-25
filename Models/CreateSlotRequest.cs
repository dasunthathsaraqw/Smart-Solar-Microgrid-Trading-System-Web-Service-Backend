/**
 * File: CreateSlotRequest.cs
 * Purpose: Request payload for POST /api/slots — creates a single energy booking slot for a station.
 *          Time-window business rules (future date, 30-day limit, duration bounds, overlap,
 *          station active) are enforced in SlotService, not here.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class CreateSlotRequest
{
    [Required]
    public string StationId { get; set; } = string.Empty;

    // Only the date part is stored; the actual window comes from StartTime and EndTime below.
    [Required]
    public DateTime SlotDate { get; set; }

    // Full date-time values (unlike BulkCreateSlotRequest, which takes times of day). Must also fall inside the station's operating schedule.
    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double CapacityKw { get; set; }
}
