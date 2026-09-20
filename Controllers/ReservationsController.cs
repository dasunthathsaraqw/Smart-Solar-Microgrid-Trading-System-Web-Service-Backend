/**
 * File: ReservationsController.cs
 * Purpose: Endpoints for the full reservation lifecycle (create, update, cancel, approve,
 *          complete) plus QR issuance and verification. Role gates are set per-endpoint since
 *          verify-qr is Grid-Operator-only while most others allow Backoffice and Grid Operator.
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
[Route("api/reservations")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;

    public ReservationsController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    // Handles GET /api/reservations?status={}&stationId={}&prosumerNic={} — lists reservations with optional filters.
    [HttpGet]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? stationId, [FromQuery] string? prosumerNic)
    {
        var reservations = await _reservationService.GetAllAsync(status, stationId, prosumerNic);
        return Ok(reservations);
    }

    // Handles POST /api/reservations/search — multi-criteria filter, sort and pagination for booking history.
    [HttpPost("search")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Search([FromBody] ReservationSearchRequest request)
    {
        try
        {
            var result = await _reservationService.SearchAsync(request);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles GET /api/reservations/{id} — returns a single reservation by id.
    [HttpGet("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetById(string id)
    {
        var reservation = await _reservationService.GetByIdAsync(id);
        if (reservation is null)
        {
            return NotFound();
        }

        return Ok(reservation);
    }

    // Handles GET /api/reservations/my — reserved for prosumer mobile login in a later stage;
    // the Prosumer role cannot currently authenticate via this API, so this is unreachable today.
    [HttpGet("my")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> GetMine()
    {
        var nic = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var reservations = await _reservationService.GetAllAsync(null, null, nic);
        return Ok(reservations);
    }

    // Handles POST /api/reservations — books a slot on behalf of a prosumer.
    [HttpPost]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Create([FromBody] CreateReservationRequest request)
    {
        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var reservation = await _reservationService.CreateAsync(request, createdBy);
            return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);
        }
        catch (ReservationConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/reservations/{id} — moves a Pending reservation to a different slot.
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateReservationRequest request)
    {
        var updatedBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var updated = await _reservationService.UpdateAsync(id, request, updatedBy);
            if (updated is null)
            {
                return NotFound();
            }

            return Ok(updated);
        }
        catch (ReservationConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/reservations/{id}/cancel — Backoffice callers may override the 12-hour rule.
    [HttpPut("{id}/cancel")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Cancel(string id, [FromBody] CancelReservationRequest request)
    {
        var cancelledBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";
        var isBackoffice = User.IsInRole("Backoffice");

        try
        {
            var cancelled = await _reservationService.CancelAsync(id, request, cancelledBy, isBackoffice);
            if (cancelled is null)
            {
                return NotFound();
            }

            return Ok(cancelled);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/reservations/{id}/approve — transitions Pending to Approved and issues a QR token.
    [HttpPut("{id}/approve")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Approve(string id)
    {
        var approvedBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var approved = await _reservationService.ApproveAsync(id, approvedBy);
            if (approved is null)
            {
                return NotFound();
            }

            return Ok(approved);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/reservations/{id}/complete — transitions Approved to Completed and frees the slot.
    [HttpPut("{id}/complete")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Complete(string id)
    {
        var completedBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var completed = await _reservationService.CompleteAsync(id, completedBy);
            if (completed is null)
            {
                return NotFound();
            }

            return Ok(completed);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles GET /api/reservations/{id}/qr — returns the QR token only while the reservation is Approved.
    [HttpGet("{id}/qr")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetQr(string id)
    {
        var reservation = await _reservationService.GetByIdAsync(id);
        if (reservation is null)
        {
            return NotFound();
        }

        var token = await _reservationService.GetQrTokenAsync(id);
        if (token is null)
        {
            return BadRequest(new { error = $"QR not available. Reservation status: {reservation.Status}" });
        }

        return Ok(new { qrToken = token });
    }

    // Handles POST /api/reservations/verify-qr — validates a prosumer's QR at the point of service.
    [HttpPost("verify-qr")]
    [Authorize(Roles = "GridOperator")]
    public async Task<IActionResult> VerifyQr([FromBody] VerifyQrRequest request)
    {
        var (valid, reservation, error) = await _reservationService.VerifyQrAsync(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        return Ok(reservation);
    }
}
