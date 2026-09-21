/**
 * File: ProsumerDashboardResponse.cs
 * Purpose: Live reservation counts and the next approved booking for a prosumer dashboard.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

public class ProsumerDashboardResponse
{
    public int PendingCount { get; set; }
    public int ApprovedFutureCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public ReservationResponse? NextReservation { get; set; }
}
