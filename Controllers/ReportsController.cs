/**
 * File: ReportsController.cs
 * Purpose: Read-only aggregate endpoints backing the Backoffice dashboard and Reports page —
 *          KPI summary, chart data, and the recent/pending booking tables.
 * Author: P.D.D.T Hemachandra it23390232
 * Date: 2026
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Roles = "Backoffice,GridOperator")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reportService;
    private readonly IUserService _userService;

    // Initializes management reporting endpoints with live report queries.
    public ReportsController(IReportService reportService, IUserService userService)
    {
        _reportService = reportService;
        _userService = userService;
    }

    // Handles GET /api/reports/dashboard-summary — top-of-dashboard KPI counters.
    [HttpGet("dashboard-summary")]
    public async Task<IActionResult> GetDashboardSummary()
    {
        var summary = await _reportService.GetDashboardSummaryAsync();
        return Ok(summary);
    }

    // Handles GET /api/reports/reservations-by-status?from=&to= — data for the status doughnut chart.
    [HttpGet("reservations-by-status")]
    public async Task<IActionResult> GetReservationsByStatus([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await _reportService.GetReservationsByStatusAsync(from, to);
        return Ok(data);
    }

    // Handles GET /api/reports/reservations-per-day?days=7 — data for the daily-count bar chart.
    [HttpGet("reservations-per-day")]
    public async Task<IActionResult> GetReservationsPerDay([FromQuery] int days = 7)
    {
        var data = await _reportService.GetReservationsPerDayAsync(days);
        return Ok(data);
    }

    // Handles GET /api/reports/top-stations?top=5&from=&to= — data for the top-stations bar chart.
    [HttpGet("top-stations")]
    public async Task<IActionResult> GetTopStations([FromQuery] int top = 5, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var data = await _reportService.GetTopStationsAsync(top, from, to);
        return Ok(data);
    }

    // Handles GET /api/reports/energy-traded?days=30 — data for the energy-traded line chart.
    [HttpGet("energy-traded")]
    public async Task<IActionResult> GetEnergyTraded([FromQuery] int days = 30)
    {
        var data = await _reportService.GetEnergyTradedAsync(days);
        return Ok(data);
    }

    // Handles GET /api/reports/recent-bookings?count=10 — the Recent Bookings table.
    [HttpGet("recent-bookings")]
    public async Task<IActionResult> GetRecentBookings([FromQuery] int count = 10)
    {
        var data = await _reportService.GetRecentBookingsAsync(count);
        return Ok(data);
    }

    // Handles GET /api/reports/pending-approvals?count=20 — the Pending Approvals queue.
    [HttpGet("pending-approvals")]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] int count = 20)
    {
        var data = await _reportService.GetPendingApprovalsAsync(count);
        return Ok(data);
    }

    /// <summary>Returns current UTC-day activity and upcoming Approved reservations for operator work.</summary>
    /// <remarks>
    /// GridOperators may omit stationId to use their persisted assignment or supply the matching assignment.
    /// A foreign stationId or missing assignment returns 403. Backoffice may omit stationId for system-wide data
    /// or provide a station. Counts and upcoming reservations are calculated live.
    /// </remarks>
    /// <param name="stationId">Optional station ObjectId; automatically resolved for GridOperators.</param>
    /// <response code="200">Returns operator dashboard counters and up to ten upcoming Approved reservations.</response>
    /// <response code="400">The resolved/requested station does not exist.</response>
    /// <response code="401">Authentication or the signed user identity is invalid.</response>
    /// <response code="403">The role is not allowed, the operator is unassigned, or a foreign station was requested.</response>
    [HttpGet("operator-dashboard")]
    [Authorize(Roles = "GridOperator,Backoffice")]
    [ProducesResponseType(typeof(OperatorDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperatorDashboard([FromQuery] string? stationId)
    {
        if (User.IsInRole("GridOperator"))
        {
            var operatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(operatorId))
            {
                return Unauthorized(new { error = "The access token does not contain a user ID claim." });
            }

            var (userExists, authorizedStationId, error) =
                await _userService.ResolveOperatorStationAsync(operatorId, stationId);
            if (!userExists)
            {
                return Unauthorized(new { error });
            }

            if (error is not null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error });
            }

            stationId = authorizedStationId;
        }

        try
        {
            var dashboard = await _reportService.GetOperatorDashboardAsync(stationId);
            return Ok(dashboard);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
