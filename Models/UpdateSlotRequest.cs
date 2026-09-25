/**
 * File: UpdateSlotRequest.cs
 * Purpose: Request payload for PUT /api/slots/{id} — partial update of an unbooked slot's timing/capacity.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

// Every property is optional; only supplied values change. If either time is supplied, the resulting start/end pair is re-validated
// (timing, station schedule, overlap). Booked slots are rejected by the service regardless of the payload.
public class UpdateSlotRequest
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double? CapacityKw { get; set; }
}
