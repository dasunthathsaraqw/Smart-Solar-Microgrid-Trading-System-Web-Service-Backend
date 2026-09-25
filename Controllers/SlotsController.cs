/**
 * File: SlotsController.cs
 * Purpose: Role-aware slot availability plus Backoffice/Grid Operator slot management endpoints.
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
[Route("api/slots")]
[Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
public class SlotsController : ControllerBase
{
    private readonly ISlotService _slotService;
    private readonly IUserService _userService;

    // Initializes slot endpoints with the slot business service and persisted-user station resolver.
    public SlotsController(ISlotService slotService, IUserService userService)
    {
        _slotService = slotService;
        _userService = userService;
    }

    // Handles GET /api/slots?stationId={id}&status={available|booked|past} — lists slots with optional filters.
    /// <remarks>Grid Operators are restricted to their persisted assigned station, including when stationId is omitted.</remarks>
    [HttpGet]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetAll([FromQuery] string? stationId, [FromQuery] string? status)
    {
        var (authorizedStationId, stationAuthorizationError) = await ResolveManagementStationScopeAsync(stationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var slots = await _slotService.GetAllAsync(authorizedStationId, status);
        return Ok(slots);
    }

    // Handles GET /api/slots/{id} — returns a single slot by id.
    /// <remarks>Grid Operators may retrieve only slots belonging to their persisted assigned station.</remarks>
    [HttpGet("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetById(string id)
    {
        var stationAuthorizationError = await AuthorizeSlotIdForOperatorAsync(id);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var slot = await _slotService.GetByIdAsync(id);
        if (slot is null)
        {
            return NotFound();
        }

        return Ok(slot);
    }

    // Handles GET /api/slots/station/{stationId} — returns all slots for a specific station.
    /// <remarks>Grid Operators may request only their persisted assigned station.</remarks>
    [HttpGet("station/{stationId}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetByStation(string stationId)
    {
        var (authorizedStationId, stationAuthorizationError) = await ResolveManagementStationScopeAsync(stationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        // Non-null here: operators get their assigned station and everyone else keeps the {stationId} route value.
        var slots = await _slotService.GetByStationAsync(authorizedStationId!);
        return Ok(slots);
    }

    // Handles GET /api/slots/station/{stationId}/available for the shared seven-day booking view.
    /// <remarks>Grid Operators are restricted to their persisted assigned station. Existing Backoffice and Prosumer access is unchanged.</remarks>
    [HttpGet("station/{stationId}/available")]
    [Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
    public async Task<IActionResult> GetAvailableByStation(string stationId)
    {
        // Prosumers pass straight through (any active station is bookable); only Grid Operators are limited to their own.
        var (authorizedStationId, stationAuthorizationError) = await ResolveManagementStationScopeAsync(stationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        try
        {
            var slots = await _slotService.GetAvailableByStationAsync(authorizedStationId!);
            return Ok(slots);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles POST /api/slots — creates a single slot.
    /// <remarks>Grid Operators may create slots only for their persisted assigned station.</remarks>
    [HttpPost]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Create([FromBody] CreateSlotRequest request)
    {
        // Only the authorization result is needed: for an operator the check fails unless request.StationId is their own station, so it can be used as-is.
        var (_, stationAuthorizationError) = await ResolveManagementStationScopeAsync(request.StationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var slot = await _slotService.CreateAsync(request, createdBy);
            return CreatedAtAction(nameof(GetById), new { id = slot.Id }, slot);
        }
        catch (SlotOverlapException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles POST /api/slots/bulk — generates multiple fixed-interval slots across a day.
    /// <remarks>Grid Operators may bulk-create slots only for their persisted assigned station.</remarks>
    [HttpPost("bulk")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreateSlotRequest request)
    {
        var (_, stationAuthorizationError) = await ResolveManagementStationScopeAsync(request.StationId);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            // 201 with the slots that were actually created; slots skipped as overlapping or out of range are simply absent (an empty list is possible).
            var slots = await _slotService.BulkCreateAsync(request, createdBy);
            return StatusCode(StatusCodes.Status201Created, slots);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/slots/{id} — updates timing/capacity of an unbooked slot.
    /// <remarks>Grid Operators may update only slots belonging to their persisted assigned station.</remarks>
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateSlotRequest request)
    {
        var stationAuthorizationError = await AuthorizeSlotIdForOperatorAsync(id);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        try
        {
            var updated = await _slotService.UpdateAsync(id, request);
            if (updated is null)
            {
                return NotFound();
            }

            return Ok(updated);
        }
        catch (SlotOverlapException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles DELETE /api/slots/{id} — deletes an unbooked slot.
    /// <remarks>Grid Operators may delete only slots belonging to their persisted assigned station.</remarks>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Delete(string id)
    {
        var stationAuthorizationError = await AuthorizeSlotIdForOperatorAsync(id);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        try
        {
            var success = await _slotService.DeleteAsync(id);
            if (!success)
            {
                return NotFound();
            }

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Resolves the signed operator and converts shared station-scope authorization failures into API responses.
    private async Task<(string? StationId, IActionResult? Error)> AuthorizeOperatorStationAsync(
        string? requestedStationId)
    {
        var operatorId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(operatorId))
        {
            return (null, Unauthorized(new { error = "The access token does not contain a user ID claim." }));
        }

        var (userExists, stationId, error) =
            await _userService.ResolveOperatorStationAsync(operatorId, requestedStationId);
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

    // Backoffice and Prosumer callers retain their existing requested scope; only Grid Operators are assignment-scoped.
    private async Task<(string? StationId, IActionResult? Error)> ResolveManagementStationScopeAsync(
        string? requestedStationId)
    {
        if (!User.IsInRole("GridOperator"))
        {
            return (requestedStationId, null);
        }

        return await AuthorizeOperatorStationAsync(requestedStationId);
    }

    // Existing-slot operations authorize before returning details or invoking any mutation.
    private async Task<IActionResult?> AuthorizeSlotIdForOperatorAsync(string id)
    {
        if (!User.IsInRole("GridOperator"))
        {
            return null;
        }

        // The operator's own station is resolved first, then the slot is loaded to compare stations.
        var (stationId, stationAuthorizationError) = await AuthorizeOperatorStationAsync(null);
        if (stationAuthorizationError is not null)
        {
            return stationAuthorizationError;
        }

        var slot = await _slotService.GetByIdAsync(id);
        if (slot is null)
        {
            return NotFound();
        }

        // A slot at another station returns 403 (not 404), so unlike the prosumer reservation endpoints it does reveal that the slot exists.
        if (!string.Equals(slot.StationId, stationId, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Grid Operator is not assigned to the requested station."
            });
        }

        return null;
    }
}
