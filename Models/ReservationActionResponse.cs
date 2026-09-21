/**
 * File: ReservationActionResponse.cs
 * Purpose: Confirmation data for a prosumer reservation action and its mobile summary screen.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class ReservationActionResponse
{
    public string Action { get; set; } = string.Empty;
    public ReservationResponse Reservation { get; set; } = new();
    public string Message { get; set; } = string.Empty;
    public double HoursUntilSlot { get; set; }
    public bool CanStillModify { get; set; }
}
