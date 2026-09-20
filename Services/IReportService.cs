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
    Task<DashboardSummary> GetDashboardSummaryAsync();
    Task<List<StatusCount>> GetReservationsByStatusAsync(DateTime? from, DateTime? to);
    Task<List<DailyCount>> GetReservationsPerDayAsync(int days);
    Task<List<StationCount>> GetTopStationsAsync(int top, DateTime? from, DateTime? to);
    Task<List<EnergyTradedPoint>> GetEnergyTradedAsync(int days);
    Task<List<RecentBooking>> GetRecentBookingsAsync(int count);
    Task<List<RecentBooking>> GetPendingApprovalsAsync(int count);
}
