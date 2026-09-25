/**
 * File: ReservationSearchRequest.cs
 * Purpose: Request payload for POST /api/reservations/search — multi-criteria filter,
 *          sort and pagination for the booking history view. Cross-field checks (date
 *          range order, sort direction) are validated in ReservationService.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.ComponentModel.DataAnnotations;

namespace SmartMicrogrid.API.Models;

public class ReservationSearchRequest
{
    // Prosumer searches always overwrite this with the signed-in NIC (SearchForProsumerAsync).
    public string? ProsumerNic { get; set; }

    // Partial, case-insensitive match against the prosumer's name.
    public string? ProsumerName { get; set; }

    // Grid Operators cannot widen this: the controller replaces it with their assigned station.
    public string? StationId { get; set; }

    // Exact match on Pending, Approved, Completed or Cancelled.
    public string? Status { get; set; }

    // Inclusive bounds on the slot's start time (not on when the booking was created).
    public DateTime? DateFrom { get; set; }

    public DateTime? DateTo { get; set; }

    // Inclusive bounds on the reserved capacity in kW.
    public double? MinCapacityKw { get; set; }

    public double? MaxCapacityKw { get; set; }

    // One of: "date" | "capacity" | "prosumer" | "station". Anything else sorts by date.
    public string? SortBy { get; set; } = "date";

    // One of: "asc" | "desc".
    public string? SortDir { get; set; } = "desc";

    // One-based page number.
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; set; } = 10;
}
