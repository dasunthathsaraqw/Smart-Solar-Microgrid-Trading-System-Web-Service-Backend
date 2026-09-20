/**
 * File: ReportModels.cs
 * Purpose: DTOs returned by ReportsController — dashboard KPIs and chart-ready aggregates.
 *          These are read models only; nothing here is persisted.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

namespace SmartMicrogrid.API.Models;

// Top-of-dashboard KPI counters, including the "Approved Future Reservations" figure
// required by the booking-views marking criterion.
public class DashboardSummary
{
    public int TotalReservations { get; set; }
    public int PendingReservations { get; set; }
    public int ApprovedReservations { get; set; }
    public int CompletedReservations { get; set; }
    public int CancelledReservations { get; set; }
    public int ActiveProsumers { get; set; }
    public int PendingProsumers { get; set; }
    public int ActiveStations { get; set; }
    public int DeactivatedStations { get; set; }
    public int ApprovedFutureReservations { get; set; }
    public int ActiveUsers { get; set; }
}

// One slice of the reservations-by-status doughnut chart.
public class StatusCount
{
    public string Status { get; set; } = string.Empty;
    public int Count { get; set; }
}

// One bar of the reservations-per-day chart.
public class DailyCount
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

// One bar of the top-stations chart.
public class StationCount
{
    public string StationId { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public int Count { get; set; }
    public double TotalKw { get; set; }
}

// One point of the energy-traded line chart.
public class EnergyTradedPoint
{
    public DateTime Date { get; set; }
    public double TotalKw { get; set; }
    public int ReservationCount { get; set; }
}

// Flat, table-ready view of a reservation for the Recent Bookings / Pending Approvals tables.
public class RecentBooking
{
    public string Id { get; set; } = string.Empty;
    public string ProsumerNic { get; set; } = string.Empty;
    public string ProsumerName { get; set; } = string.Empty;
    public string StationName { get; set; } = string.Empty;
    public DateTime SlotStartTime { get; set; }
    public DateTime SlotEndTime { get; set; }
    public double CapacityKw { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
