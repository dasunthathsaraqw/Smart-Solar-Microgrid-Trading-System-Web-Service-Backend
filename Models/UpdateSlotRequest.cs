/**
 * File: UpdateSlotRequest.cs
 * Purpose: Request payload for PUT /api/slots/{id} — partial update of an unbooked slot's timing/capacity.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class UpdateSlotRequest
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    [Range(0.01, 1000000, ErrorMessage = "Capacity must be greater than 0.")]
    public double? CapacityKw { get; set; }
}
