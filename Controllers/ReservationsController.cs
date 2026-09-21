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

// Authorization remains per action because existing routes have different roles and class-level gates combine with them.
[ApiController]
[Route("api/reservations")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;

    // Initializes the role-gated reservation endpoints with their business service.
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

    // Handles GET /api/reservations/operator/history for completed Grid Operator transaction history.
    [HttpGet("operator/history")]
    [Authorize(Roles = "GridOperator")]
    public async Task<IActionResult> GetOperatorTransactionHistory(
        [FromQuery] string? stationId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await _reservationService.GetOperatorTransactionHistoryAsync(
                stationId,
                dateFrom,
                dateTo,
                page,
                pageSize);
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

    // Handles GET /api/reservations/my using the signed NIC claim and optional status filter.
    [HttpGet("my")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> GetMine([FromQuery] string? status)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        var reservations = await _reservationService.GetByProsumerAsync(nic, status);
        return Ok(reservations);
    }

    // Handles POST /api/reservations/my/search while forcing search ownership to the signed NIC.
    [HttpPost("my/search")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> SearchMine([FromBody] ReservationSearchRequest request)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        try
        {
            var results = await _reservationService.SearchForProsumerAsync(nic, request);
            return Ok(results);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles GET /api/reservations/my/{id} without revealing ownership of other reservations.
    [HttpGet("my/{id}")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> GetMineById(string id)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        // A 404 hides both nonexistent IDs and IDs owned by others; a 403 would disclose their existence.
        if (!await _reservationService.IsOwnedByAsync(id, nic))
        {
            return NotFound();
        }

        var reservation = await _reservationService.GetByIdAsync(id);
        return reservation is null ? NotFound() : Ok(reservation);
    }

    // Handles POST /api/reservations/my and returns a server-computed confirmation summary.
    [HttpPost("my")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> CreateMine([FromBody] CreateOwnReservationRequest request)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        try
        {
            var reservationRequest = new CreateReservationRequest
            {
                ProsumerNic = request.ProsumerNic ?? string.Empty,
                StationId = request.StationId,
                SlotId = request.SlotId,
            };
            var reservation = await _reservationService.CreateForProsumerAsync(reservationRequest, nic);
            var summary = _reservationService.CreateActionResponse(reservation, "Created");
            return CreatedAtAction(nameof(GetMineById), new { id = reservation.Id }, summary);
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

    // Handles PUT /api/reservations/my/{id} after a non-disclosing ownership check.
    [HttpPut("my/{id}")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> UpdateMine(string id, [FromBody] UpdateReservationRequest request)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        // A 404 hides both nonexistent IDs and IDs owned by others; a 403 would disclose their existence.
        if (!await _reservationService.IsOwnedByAsync(id, nic))
        {
            return NotFound();
        }

        try
        {
            var reservation = await _reservationService.UpdateAsync(id, request, nic);
            return reservation is null
                ? NotFound()
                : Ok(_reservationService.CreateActionResponse(reservation, "Updated"));
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

    // Handles PUT /api/reservations/my/{id}/cancel without allowing a notice-rule override.
    [HttpPut("my/{id}/cancel")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> CancelMine(string id, [FromBody] CancelReservationRequest request)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        // A 404 hides both nonexistent IDs and IDs owned by others; a 403 would disclose their existence.
        if (!await _reservationService.IsOwnedByAsync(id, nic))
        {
            return NotFound();
        }

        try
        {
            var reservation = await _reservationService.CancelAsync(id, request, nic, allowOverride: false);
            return reservation is null
                ? NotFound()
                : Ok(_reservationService.CreateActionResponse(reservation, "Cancelled"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles GET /api/reservations/my/{id}/qr only for an owned, approved reservation.
    [HttpGet("my/{id}/qr")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> GetMineQr(string id)
    {
        var nic = GetTokenNic();
        if (nic is null)
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        // A 404 hides both nonexistent IDs and IDs owned by others; a 403 would disclose their existence.
        if (!await _reservationService.IsOwnedByAsync(id, nic))
        {
            return NotFound();
        }

        var token = await _reservationService.GetQrTokenAsync(id);
        return token is null ? NotFound() : Ok(new { qrToken = token });
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

    // Handles POST /api/reservations/scan-complete in one server-side QR validation and completion call.
    [HttpPost("scan-complete")]
    [Authorize(Roles = "GridOperator")]
    public async Task<IActionResult> ScanComplete([FromBody] VerifyQrRequest request)
    {
        var completedBy = User.FindFirstValue(ClaimTypes.Email);
        if (completedBy is null)
        {
            return Unauthorized(new { error = "The access token does not contain an email claim." });
        }

        // Operators have no station assignment in User, so StationId comes from the app's selected station.
        // VerifyQrAsync checks that selection; server-bound operator station authorization remains a known limitation.
        var (success, reservation, error) = await _reservationService.ScanAndCompleteAsync(request, completedBy);
        return success ? Ok(reservation) : BadRequest(new { error });
    }

    // Reads the authenticated prosumer's immutable NIC claim for self-service requests.
    private string? GetTokenNic()
    {
        var nic = User.FindFirst("nic")?.Value;
        return string.IsNullOrWhiteSpace(nic) ? null : nic;
    }
}
