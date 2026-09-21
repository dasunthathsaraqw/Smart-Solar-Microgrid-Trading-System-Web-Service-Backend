/**
 * File: OperatorDashboardResponse.cs
 * Purpose: Live UTC-day activity and upcoming approved bookings for an operator dashboard.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class OperatorDashboardResponse
{
    public int PendingToday { get; set; }
    public int ApprovedToday { get; set; }
    public int CompletedToday { get; set; }
    public int ApprovedFutureCount { get; set; }
    public List<ReservationResponse> UpcomingApproved { get; set; } = new();
}
