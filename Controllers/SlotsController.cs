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

    // Initializes slot endpoints with the slot business service.
    public SlotsController(ISlotService slotService)
    {
        _slotService = slotService;
    }

    // Handles GET /api/slots?stationId={id}&status={available|booked|past} — lists slots with optional filters.
    [HttpGet]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetAll([FromQuery] string? stationId, [FromQuery] string? status)
    {
        var slots = await _slotService.GetAllAsync(stationId, status);
        return Ok(slots);
    }

    // Handles GET /api/slots/{id} — returns a single slot by id.
    [HttpGet("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetById(string id)
    {
        var slot = await _slotService.GetByIdAsync(id);
        if (slot is null)
        {
            return NotFound();
        }

        return Ok(slot);
    }

    // Handles GET /api/slots/station/{stationId} — returns all slots for a specific station.
    [HttpGet("station/{stationId}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> GetByStation(string stationId)
    {
        var slots = await _slotService.GetByStationAsync(stationId);
        return Ok(slots);
    }

    // Handles GET /api/slots/station/{stationId}/available for the shared seven-day booking view.
    [HttpGet("station/{stationId}/available")]
    [Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
    public async Task<IActionResult> GetAvailableByStation(string stationId)
    {
        try
        {
            var slots = await _slotService.GetAvailableByStationAsync(stationId);
            return Ok(slots);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles POST /api/slots — creates a single slot.
    [HttpPost]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Create([FromBody] CreateSlotRequest request)
    {
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
    [HttpPost("bulk")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> BulkCreate([FromBody] BulkCreateSlotRequest request)
    {
        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var slots = await _slotService.BulkCreateAsync(request, createdBy);
            return StatusCode(StatusCodes.Status201Created, slots);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // Handles PUT /api/slots/{id} — updates timing/capacity of an unbooked slot.
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateSlotRequest request)
    {
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
    [HttpDelete("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator")]
    public async Task<IActionResult> Delete(string id)
    {
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
}
