/**
 * File: IReportService.cs
 * Purpose: Contract for dashboard KPI and chart-ready aggregate queries. Every method reads
 *          straight from MongoDB on each call — nothing here is cached.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public interface IReportService
{
    // Returns all-system dashboard counters for management clients.
    Task<DashboardSummary> GetDashboardSummaryAsync();

    // Groups reservations by status within an optional date range.
    Task<List<StatusCount>> GetReservationsByStatusAsync(DateTime? from, DateTime? to);

    // Returns daily reservation counts for a recent date window.
    Task<List<DailyCount>> GetReservationsPerDayAsync(int days);

    // Ranks stations by reservation activity.
    Task<List<StationCount>> GetTopStationsAsync(int top, DateTime? from, DateTime? to);

    // Returns daily completed-transfer capacity.
    Task<List<EnergyTradedPoint>> GetEnergyTradedAsync(int days);

    // Returns the most recently created reservation summaries.
    Task<List<RecentBooking>> GetRecentBookingsAsync(int count);

    // Returns the upcoming pending-approval queue.
    Task<List<RecentBooking>> GetPendingApprovalsAsync(int count);

    // Returns live status counts and the next approved booking for one prosumer.
    Task<ProsumerDashboardResponse> GetProsumerDashboardAsync(string prosumerNic);

    // Returns live UTC-day activity and upcoming bookings for one station or the entire grid.
    Task<OperatorDashboardResponse> GetOperatorDashboardAsync(string? stationId);
}
