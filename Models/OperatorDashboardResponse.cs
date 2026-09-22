/**
 * File: OperatorDashboardResponse.cs
 * Purpose: Live UTC-day activity and upcoming approved bookings for an operator dashboard.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class OperatorDashboardResponse
{
    /// <summary>Pending reservations whose slot starts during the current UTC day.</summary>
    public int PendingToday { get; set; }

    /// <summary>Approved reservations whose slot starts during the current UTC day.</summary>
    public int ApprovedToday { get; set; }

    /// <summary>Reservations completed during the current UTC day.</summary>
    public int CompletedToday { get; set; }

    /// <summary>Total Approved reservations with a slot start later than the current UTC instant.</summary>
    public int ApprovedFutureCount { get; set; }

    /// <summary>The next ten future Approved reservations, ordered by slot start time.</summary>
    public List<ReservationResponse> UpcomingApproved { get; set; } = new();
}
