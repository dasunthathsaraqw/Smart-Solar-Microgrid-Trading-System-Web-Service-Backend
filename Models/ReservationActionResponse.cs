/**
 * File: ReservationActionResponse.cs
 * Purpose: Confirmation data for a prosumer reservation action and its mobile summary screen.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class ReservationActionResponse
{
    // "Created", "Updated" or "Cancelled"; the service rejects any other value.
    public string Action { get; set; } = string.Empty;
    public ReservationResponse Reservation { get; set; } = new();
    // Ready-to-display confirmation text, so the mobile app carries no wording of its own.
    public string Message { get; set; } = string.Empty;
    // Hours from now until the slot starts, rounded to 1 decimal for display only. Negative if the slot has started.
    public double HoursUntilSlot { get; set; }
    // True only for a Pending reservation with at least 12 hours left, i.e. when an update would still be accepted (Approved bookings are never editable).
    public bool CanStillModify { get; set; }
}
