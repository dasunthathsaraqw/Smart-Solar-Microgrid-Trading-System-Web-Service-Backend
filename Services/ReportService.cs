/**
 * File: ReportService.cs
 * Purpose: Implements dashboard KPI and chart-ready aggregate queries against MongoDB.
 *          Aggregation is done in-memory over the (assignment-scale) collections after a
 *          filtered fetch — simple and correct, and still a live read on every call.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using MongoDB.Bson;
using MongoDB.Driver;
using SmartMicrogrid.API.Models;

namespace SmartMicrogrid.API.Services;

public class ReportService : IReportService
{
    private readonly IMongoDbService _db;

    // Initializes report queries with access to the live MongoDB collections.
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

    // Aggregates one prosumer's statuses in one query, then finds their next future approved booking.
    public async Task<ProsumerDashboardResponse> GetProsumerDashboardAsync(string prosumerNic)
    {
        var now = DateTime.UtcNow;
        var statusCounts = await _db.Reservations.Aggregate()
            .Match(reservation => reservation.ProsumerNic == prosumerNic)
            .Group(reservation => reservation.Status, group => new
            {
                Status = group.Key,
                Count = group.Count(),
                FutureCount = group.Sum(reservation => reservation.SlotStartTime > now ? 1 : 0),
            })
            .ToListAsync();

        var next = await _db.Reservations.Find(reservation =>
                reservation.ProsumerNic == prosumerNic &&
                reservation.Status == "Approved" &&
                reservation.SlotStartTime > now)
            .SortBy(reservation => reservation.SlotStartTime)
            .FirstOrDefaultAsync();

        return new ProsumerDashboardResponse
        {
            PendingCount = statusCounts.FirstOrDefault(group => group.Status == "Pending")?.Count ?? 0,
            ApprovedFutureCount = statusCounts.FirstOrDefault(group => group.Status == "Approved")?.FutureCount ?? 0,
            CompletedCount = statusCounts.FirstOrDefault(group => group.Status == "Completed")?.Count ?? 0,
            CancelledCount = statusCounts.FirstOrDefault(group => group.Status == "Cancelled")?.Count ?? 0,
            NextReservation = next is null ? null : ToReservationResponse(next),
        };
    }

    // Counts current-UTC-day activity and upcoming approved bookings for an optional station scope.
    public async Task<OperatorDashboardResponse> GetOperatorDashboardAsync(string? stationId)
    {
        if (!string.IsNullOrWhiteSpace(stationId) &&
            (!ObjectId.TryParse(stationId, out _) ||
             !await _db.Stations.Find(station => station.Id == stationId).AnyAsync()))
        {
            throw new InvalidOperationException("Station not found");
        }

        var filter = string.IsNullOrWhiteSpace(stationId)
            ? FilterDefinition<EnergyReservation>.Empty
            : Builders<EnergyReservation>.Filter.Eq(reservation => reservation.StationId, stationId);
        var reservations = await _db.Reservations.Find(filter).ToListAsync();
        var now = DateTime.UtcNow;
        var today = now.Date;
        var tomorrow = today.AddDays(1);

        var upcoming = reservations
            .Where(reservation => reservation.Status == "Approved" && reservation.SlotStartTime > now)
            .OrderBy(reservation => reservation.SlotStartTime)
            .ToList();

        return new OperatorDashboardResponse
        {
            PendingToday = reservations.Count(reservation => reservation.Status == "Pending" &&
                reservation.SlotStartTime >= today && reservation.SlotStartTime < tomorrow),
            ApprovedToday = reservations.Count(reservation => reservation.Status == "Approved" &&
                reservation.SlotStartTime >= today && reservation.SlotStartTime < tomorrow),
            CompletedToday = reservations.Count(reservation => reservation.Status == "Completed" &&
                reservation.CompletedAt.HasValue && reservation.CompletedAt.Value >= today &&
                reservation.CompletedAt.Value < tomorrow),
            ApprovedFutureCount = upcoming.Count,
            UpcomingApproved = upcoming.Take(10).Select(ToReservationResponse).ToList(),
        };
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

    // Maps a live reservation document to the response shape used by mobile dashboards.
    private static ReservationResponse ToReservationResponse(EnergyReservation reservation)
    {
        return new ReservationResponse
        {
            Id = reservation.Id,
            ProsumerNic = reservation.ProsumerNic,
            ProsumerName = reservation.ProsumerName,
            StationId = reservation.StationId,
            StationName = reservation.StationName,
            SlotId = reservation.SlotId,
            SlotStartTime = reservation.SlotStartTime,
            SlotEndTime = reservation.SlotEndTime,
            CapacityKw = reservation.CapacityKw,
            Status = reservation.Status,
            QrGeneratedAt = reservation.QrGeneratedAt,
            CreatedAt = reservation.CreatedAt,
            CreatedBy = reservation.CreatedBy,
            UpdatedAt = reservation.UpdatedAt,
            ApprovedAt = reservation.ApprovedAt,
            ApprovedBy = reservation.ApprovedBy,
            CompletedAt = reservation.CompletedAt,
            CompletedBy = reservation.CompletedBy,
            CancelledAt = reservation.CancelledAt,
            CancelledBy = reservation.CancelledBy,
            CancellationReason = reservation.CancellationReason,
        };
    }
}
