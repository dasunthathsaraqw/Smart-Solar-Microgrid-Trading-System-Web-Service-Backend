/**
 * File: ReportService.cs
 * Purpose: Implements dashboard KPI and chart-ready aggregate queries against MongoDB.
 *          Aggregation is done in-memory over the (assignment-scale) collections after a
 *          filtered fetch — simple and correct, and still a live read on every call.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class ReportService : IReportService
{
    private readonly IMongoDbService _db;

    public ReportService(IMongoDbService db)
    {
        _db = db;
    }

    // Builds the top-of-dashboard KPI counters, including approved reservations still in the future.
    public async Task<DashboardSummary> GetDashboardSummaryAsync()
    {
        var reservations = await _db.Reservations.Find(FilterDefinition<EnergyReservation>.Empty).ToListAsync();
        var prosumers = await _db.Prosumers.Find(FilterDefinition<Prosumer>.Empty).ToListAsync();
        var stations = await _db.Stations.Find(FilterDefinition<SolarStationInfo>.Empty).ToListAsync();
        var users = await _db.Users.Find(FilterDefinition<User>.Empty).ToListAsync();
        var now = DateTime.UtcNow;

        return new DashboardSummary
        {
            TotalReservations = reservations.Count,
            PendingReservations = reservations.Count(r => r.Status == "Pending"),
            ApprovedReservations = reservations.Count(r => r.Status == "Approved"),
            CompletedReservations = reservations.Count(r => r.Status == "Completed"),
            CancelledReservations = reservations.Count(r => r.Status == "Cancelled"),
            ActiveProsumers = prosumers.Count(p => p.IsActive),
            PendingProsumers = prosumers.Count(p => !p.IsActive && !p.DeactivationRequested),
            ActiveStations = stations.Count(s => s.IsActive),
            DeactivatedStations = stations.Count(s => !s.IsActive),
            ApprovedFutureReservations = reservations.Count(r => r.Status == "Approved" && r.SlotStartTime > now),
            ActiveUsers = users.Count(u => u.IsActive),
        };
    }

    // Groups reservations (optionally within a date range, by CreatedAt) by status, for the status doughnut chart.
    public async Task<List<StatusCount>> GetReservationsByStatusAsync(DateTime? from, DateTime? to)
    {
        var reservations = await _db.Reservations.Find(BuildCreatedAtRangeFilter(from, to)).ToListAsync();

        return reservations
            .GroupBy(r => r.Status)
            .Select(g => new StatusCount { Status = g.Key, Count = g.Count() })
            .OrderByDescending(s => s.Count)
            .ToList();
    }

    // Counts reservations created per day over the last N days (including days with zero), for the bar chart.
    public async Task<List<DailyCount>> GetReservationsPerDayAsync(int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var reservations = await _db.Reservations.Find(r => r.CreatedAt >= since).ToListAsync();

        var counts = reservations.GroupBy(r => r.CreatedAt.Date).ToDictionary(g => g.Key, g => g.Count());

        var result = new List<DailyCount>();
        for (var i = 0; i < days; i++)
        {
            var date = since.AddDays(i);
            result.Add(new DailyCount { Date = date, Count = counts.GetValueOrDefault(date) });
        }

        return result;
    }

    // Ranks stations by reservation count (optionally within a date range), for the top-stations chart.
    public async Task<List<StationCount>> GetTopStationsAsync(int top, DateTime? from, DateTime? to)
    {
        var reservations = await _db.Reservations.Find(BuildCreatedAtRangeFilter(from, to)).ToListAsync();

        return reservations
            .GroupBy(r => new { r.StationId, r.StationName })
            .Select(g => new StationCount
            {
                StationId = g.Key.StationId,
                StationName = g.Key.StationName,
                Count = g.Count(),
                TotalKw = g.Where(r => r.Status == "Completed").Sum(r => r.CapacityKw),
            })
            .OrderByDescending(s => s.Count)
            .Take(top)
            .ToList();
    }

    // Sums completed reservations' capacity per day over the last N days, for the energy-traded line chart.
    public async Task<List<EnergyTradedPoint>> GetEnergyTradedAsync(int days)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var reservations = await _db.Reservations
            .Find(r => r.Status == "Completed" && r.CompletedAt != null && r.CompletedAt >= since)
            .ToListAsync();

        var grouped = reservations
            .GroupBy(r => r.CompletedAt!.Value.Date)
            .ToDictionary(g => g.Key, g => (TotalKw: g.Sum(r => r.CapacityKw), Count: g.Count()));

        var result = new List<EnergyTradedPoint>();
        for (var i = 0; i < days; i++)
        {
            var date = since.AddDays(i);
            grouped.TryGetValue(date, out var data);
            result.Add(new EnergyTradedPoint { Date = date, TotalKw = data.TotalKw, ReservationCount = data.Count });
        }

        return result;
    }

    // Returns the most recently created reservations, newest first, for the Recent Bookings table.
    public async Task<List<RecentBooking>> GetRecentBookingsAsync(int count)
    {
        var reservations = await _db.Reservations.Find(FilterDefinition<EnergyReservation>.Empty)
            .SortByDescending(r => r.CreatedAt)
            .Limit(count)
            .ToListAsync();

        return reservations.Select(ToRecentBooking).ToList();
    }

    // Returns the oldest-first queue of Pending reservations, for the Pending Approvals table.
    public async Task<List<RecentBooking>> GetPendingApprovalsAsync(int count)
    {
        var reservations = await _db.Reservations.Find(r => r.Status == "Pending")
            .SortBy(r => r.SlotStartTime)
            .Limit(count)
            .ToListAsync();

        return reservations.Select(ToRecentBooking).ToList();
    }

    // Builds an optional [from, to] filter over CreatedAt, used by the status and top-stations reports.
    private static FilterDefinition<EnergyReservation> BuildCreatedAtRangeFilter(DateTime? from, DateTime? to)
    {
        var filters = new List<FilterDefinition<EnergyReservation>>();

        if (from.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Gte(r => r.CreatedAt, from.Value));
        }

        if (to.HasValue)
        {
            filters.Add(Builders<EnergyReservation>.Filter.Lte(r => r.CreatedAt, to.Value));
        }

        return filters.Count > 0 ? Builders<EnergyReservation>.Filter.And(filters) : FilterDefinition<EnergyReservation>.Empty;
    }

    // Maps an EnergyReservation document to the flat table row shape used by both booking tables.
    private static RecentBooking ToRecentBooking(EnergyReservation r)
    {
        return new RecentBooking
        {
            Id = r.Id,
            ProsumerNic = r.ProsumerNic,
            ProsumerName = r.ProsumerName,
            StationName = r.StationName,
            SlotStartTime = r.SlotStartTime,
            SlotEndTime = r.SlotEndTime,
            CapacityKw = r.CapacityKw,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
        };
    }
}
