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
    private readonly IUserService _userService;
    private readonly ISlotService _slotService;

    // Initializes the role-gated reservation endpoints with their business service.
    public ReservationsController(
        IReservationService reservationService,
        IUserService userService,
        ISlotService slotService)
    {
        _reservationService = reservationService;
        _userService = userService;
        _slotService = slotService;
    }

    // Handles GET /api/reservations?status={}&stationId={}&prosumerNic={} — lists reservations with optional filters.
    [HttpGet]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? stationId, [FromQuery] string? prosumerNic)
    {
        // Grid Operators are pinned to their own station whatever stationId they pass; Backoffice keeps the station it asked for.
        var (authorizedStationId, authorizationError) = await ResolveManagementStationScopeAsync(stationId);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        var reservations = await _reservationService.GetAllAsync(status, authorizedStationId, prosumerNic);
        return Ok(reservations);
    }

    // Handles POST /api/reservations/search — multi-criteria filter, sort and pagination for booking history.
    [HttpPost("search")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Search([FromBody] ReservationSearchRequest request)
    {
        var (authorizedStationId, authorizationError) = await ResolveManagementStationScopeAsync(request.StationId);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        // Grid Operators cannot widen the search beyond their persisted station.
        request.StationId = authorizedStationId;

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

    /// <summary>Returns the signed GridOperator's Completed reservation history.</summary>
    /// <remarks>
    /// The persisted station assignment is used when stationId is omitted. A matching explicit stationId is
    /// accepted; a foreign station or missing assignment returns 403. Results contain only Completed reservations,
    /// are ordered by CompletedAt descending, and use the standard paged response envelope.
    /// </remarks>
    /// <param name="stationId">Optional assigned station ObjectId.</param>
    /// <param name="dateFrom">Optional inclusive lower bound for CompletedAt.</param>
    /// <param name="dateTo">Optional inclusive upper bound for CompletedAt.</param>
    /// <param name="page">One-based page number; default 1.</param>
    /// <param name="pageSize">Items per page from 1 through 100; default 10.</param>
    /// <response code="200">Returns a page of Completed reservations at the assigned station.</response>
    /// <response code="400">Pagination, date range, or resolved-station validation failed.</response>
    /// <response code="401">Authentication or the signed operator identity is invalid.</response>
    /// <response code="403">The caller is not a GridOperator, is unassigned, or requested a foreign station.</response>
    [HttpGet("operator/history")]
    [Authorize(Roles = "GridOperator")]
    [ProducesResponseType(typeof(PagedResult<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetOperatorTransactionHistory(
        [FromQuery] string? stationId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var (authorizedStationId, stationAuthorizationError) = await AuthorizeOperatorStationAsync(stationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        try
        {
            var result = await _reservationService.GetOperatorTransactionHistoryAsync(
                authorizedStationId,
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

        // The reservation is loaded first so an operator can be checked against its station; Backoffice passes through.
        var authorizationError = await AuthorizeReservationForOperatorAsync(reservation);
        if (authorizationError is not null)
        {
            return authorizationError;
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
            // Any NIC in the body is ignored: CreateForProsumerAsync overwrites it with the token's NIC.
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
            // Slot already booked: 409 so the app can tell "pick another slot" apart from other validation failures (400).
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
            // The prosumer's NIC is recorded as the actor in place of an email.
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

        // No token (reservation not yet approved, or already completed/cancelled) is reported as 404, not as an error explaining why.
        var token = await _reservationService.GetQrTokenAsync(id);
        return token is null ? NotFound() : Ok(new { qrToken = token });
    }

    // Handles POST /api/reservations — books a slot on behalf of a prosumer.
    [HttpPost]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Create([FromBody] CreateReservationRequest request)
    {
        var (authorizedStationId, authorizationError) = await ResolveManagementStationScopeAsync(request.StationId);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        // Replaces the requested station with the authorized one, so an operator cannot book at another station.
        // The null-forgiving operator is safe: Backoffice keeps the [Required] StationId, and operators are only let through with a resolved station.
        request.StationId = authorizedStationId!;
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
        if (User.IsInRole("GridOperator"))
        {
            var existingReservation = await _reservationService.GetByIdAsync(id);
            if (existingReservation is null)
            {
                return NotFound();
            }

            var authorizationError = await AuthorizeReservationForOperatorAsync(existingReservation);
            if (authorizationError is not null)
            {
                return authorizationError;
            }

            // The reservation's current station was checked above; the target slot must also be at the operator's station.
            // An unknown slot skips this check and is reported by the service as "Slot not found".
            var destinationSlot = await _slotService.GetByIdAsync(request.NewSlotId);
            if (destinationSlot is not null &&
                !string.Equals(destinationSlot.StationId, existingReservation.StationId, StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    error = "Grid Operator is not assigned to the requested station."
                });
            }
        }

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
        var authorizationError = await AuthorizeReservationIdForOperatorAsync(id);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        var cancelledBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";
        // Only Backoffice may bypass the 12-hour notice rule; Grid Operators are held to it.
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
        var authorizationError = await AuthorizeReservationIdForOperatorAsync(id);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

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

    /// <summary>Administratively completes an Approved reservation without scanning its QR.</summary>
    /// <remarks>
    /// This is a Backoffice recovery/administrative path. GridOperators receive 403 and must use
    /// POST /api/reservations/scan-complete so the QR, station, status, and time window are verified.
    /// Successful Backoffice completion records CompletedAt/CompletedBy, invalidates the QR, and releases the slot.
    /// </remarks>
    /// <param name="id">Reservation ObjectId.</param>
    /// <response code="200">The Backoffice caller completed the Approved reservation.</response>
    /// <response code="400">The reservation is not Approved.</response>
    /// <response code="401">Authentication is missing or invalid.</response>
    /// <response code="403">A GridOperator or another disallowed role attempted direct completion.</response>
    /// <response code="404">No reservation exists with the supplied ID.</response>
    [HttpPut("{id}/complete")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(string id)
    {
        if (User.IsInRole("GridOperator"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Grid Operators must complete energy transfers through QR verification."
            });
        }

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

    /// <summary>Returns an Approved reservation's QR token to an authorized management caller.</summary>
    /// <remarks>
    /// GridOperators may retrieve QR data only for their persisted station. Backoffice retains administrative
    /// access. The token is returned only while the reservation remains Approved.
    /// </remarks>
    /// <param name="id">Reservation ObjectId.</param>
    /// <response code="200">Returns an object containing qrToken.</response>
    /// <response code="400">The reservation is not Approved or has no available QR token.</response>
    /// <response code="401">Authentication or the signed operator identity is invalid.</response>
    /// <response code="403">The role is disallowed, the operator is unassigned, or the reservation is foreign.</response>
    /// <response code="404">No reservation exists with the supplied ID.</response>
    [HttpGet("{id}/qr")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQr(string id)
    {
        var reservation = await _reservationService.GetByIdAsync(id);
        if (reservation is null)
        {
            return NotFound();
        }

        var authorizationError = await AuthorizeReservationForOperatorAsync(reservation);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        var token = await _reservationService.GetQrTokenAsync(id);
        if (token is null)
        {
            return BadRequest(new { error = $"QR not available. Reservation status: {reservation.Status}" });
        }

        return Ok(new { qrToken = token });
    }

    /// <summary>Validates a presented reservation QR for the signed GridOperator's station.</summary>
    /// <remarks>
    /// The request stationId must match the operator's persisted assignment. Validation then requires a recognized
    /// token, an Approved reservation, the same reservation station, and a slot start within the +/-24-hour window.
    /// This endpoint does not mutate the reservation; call scan-complete only after physical-transfer confirmation.
    /// </remarks>
    /// <param name="request">The QR token and station where it is presented.</param>
    /// <response code="200">The QR is valid; returns the corresponding ReservationResponse.</response>
    /// <response code="400">Validation failed, including an unknown/reused token, wrong QR station, status, or time window.</response>
    /// <response code="401">Authentication or the signed operator identity is invalid.</response>
    /// <response code="403">The caller is not a GridOperator, is unassigned, or requested a foreign station.</response>
    [HttpPost("verify-qr")]
    [Authorize(Roles = "GridOperator")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> VerifyQr([FromBody] VerifyQrRequest request)
    {
        var (_, stationAuthorizationError) = await AuthorizeOperatorStationAsync(request.StationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var (valid, reservation, error) = await _reservationService.VerifyQrAsync(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        return Ok(reservation);
    }

    /// <summary>Verifies a presented QR and atomically completes the physical energy transfer.</summary>
    /// <remarks>
    /// This is the required GridOperator completion path. It applies the same station, token, Approved-status, and
    /// +/-24-hour checks as verify-qr, then atomically claims Approved-to-Completed. Success records CompletedAt and
    /// CompletedBy, invalidates the QR, releases the slot, and prevents replay/concurrent double completion.
    /// </remarks>
    /// <param name="request">The previously verified QR token and assigned station ID.</param>
    /// <response code="200">The transfer was completed; returns the completed ReservationResponse.</response>
    /// <response code="400">QR validation failed or another scan already completed the reservation.</response>
    /// <response code="401">Authentication or required signed operator identity claims are invalid.</response>
    /// <response code="403">The caller is not a GridOperator, is unassigned, or requested a foreign station.</response>
    [HttpPost("scan-complete")]
    [Authorize(Roles = "GridOperator")]
    [ProducesResponseType(typeof(ReservationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ScanComplete([FromBody] VerifyQrRequest request)
    {
        var (_, stationAuthorizationError) = await AuthorizeOperatorStationAsync(request.StationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        // The completion is audited by email, so a token without one is rejected instead of falling back to "unknown".
        var completedBy = User.FindFirstValue(ClaimTypes.Email);
        if (completedBy is null)
        {
            return Unauthorized(new { error = "The access token does not contain an email claim." });
        }

        var (success, reservation, error) = await _reservationService.ScanAndCompleteAsync(request, completedBy);
        return success ? Ok(reservation) : BadRequest(new { error });
    }

    // Resolves the signed operator and converts shared station-scope authorization failures into API responses.
    private async Task<(string? StationId, IActionResult? Error)> AuthorizeOperatorStationAsync(string? requestStationId)
    {
        var operatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return (null, Unauthorized(new { error = "The access token does not contain a user ID claim." }));
        }

        // Two failure levels: the operator account no longer exists (401, the token is stale), versus the account
        // exists but has no station or asked for another one (403, authenticated but not allowed).
        var (userExists, stationId, error) = await _userService.ResolveOperatorStationAsync(operatorId, requestStationId);
        if (!userExists)
        {
            return (null, Unauthorized(new { error }));
        }

        if (error is not null)
        {
            return (null, StatusCode(StatusCodes.Status403Forbidden, new { error }));
        }

        return (stationId, null);
    }

    // Backoffice retains its requested scope; Grid Operators always use their persisted station assignment.
    private async Task<(string? StationId, IActionResult? Error)> ResolveManagementStationScopeAsync(
        string? requestStationId)
    {
        if (!User.IsInRole("GridOperator"))
        {
            return (requestStationId, null);
        }

        return await AuthorizeOperatorStationAsync(requestStationId);
    }

    // Loads only for Grid Operators so Backoffice behavior and service-side not-found handling remain unchanged.
    private async Task<IActionResult?> AuthorizeReservationIdForOperatorAsync(string id)
    {
        if (!User.IsInRole("GridOperator"))
        {
            return null;
        }

        var reservation = await _reservationService.GetByIdAsync(id);
        return reservation is null
            ? NotFound()
            : await AuthorizeReservationForOperatorAsync(reservation);
    }

    // Existing-reservation actions must not disclose or mutate another station's reservation.
    private async Task<IActionResult?> AuthorizeReservationForOperatorAsync(ReservationResponse reservation)
    {
        if (!User.IsInRole("GridOperator"))
        {
            return null;
        }

        // Passing null asks for the operator's own persisted station, which the reservation must then belong to.
        var (stationId, authorizationError) = await AuthorizeOperatorStationAsync(null);
        if (authorizationError is not null)
        {
            return authorizationError;
        }

        if (!string.Equals(reservation.StationId, stationId, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Grid Operator is not assigned to this reservation's station."
            });
        }

        return null;
    }

    // Reads the authenticated prosumer's immutable NIC claim for self-service requests.
    private string? GetTokenNic()
    {
        var nic = User.FindFirst("nic")?.Value;
        return string.IsNullOrWhiteSpace(nic) ? null : nic;
    }
}
