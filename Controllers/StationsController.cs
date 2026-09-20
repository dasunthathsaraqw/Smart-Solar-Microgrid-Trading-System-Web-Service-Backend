/**
 * File: StationsController.cs
 * Purpose: Backoffice-only endpoints for microgrid station registration, lookup, update and lifecycle actions.
 * Author: <Your Name>
 * Date: 2026
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

[ApiController]
[Route("api/stations")]
[Authorize(Roles = "Backoffice")]
public class StationsController : ControllerBase
{
    private readonly IStationService _stationService;

    public StationsController(IStationService stationService)
    {
        _stationService = stationService;
    }

    // Handles GET /api/stations?status={active|deactivated} — lists stations, optionally filtered by status.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var stations = await _stationService.GetAllAsync(status);
        return Ok(stations);
    }

    // Handles GET /api/stations/{id} — returns a single station by id.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var station = await _stationService.GetByIdAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        return Ok(station);
    }

    // Handles POST /api/stations — registers a new microgrid station.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStationRequest request)
    {
        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var station = await _stationService.CreateAsync(request, createdBy);
            return CreatedAtAction(nameof(GetById), new { id = station.Id }, station);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // Handles PUT /api/stations/{id} — updates the editable fields of a station.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateStationRequest request)
    {
        try
        {
            var updated = await _stationService.UpdateAsync(id, request);
            if (updated is null)
            {
                return NotFound();
            }

            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // Handles PUT /api/stations/{id}/deactivate — deactivates a station unless active reservations block it.
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var (success, error) = await _stationService.DeactivateAsync(id);
        if (!success)
        {
            return error == "Station not found" ? NotFound() : BadRequest(new { error });
        }

        return NoContent();
    }

    // Handles PUT /api/stations/{id}/reactivate — reactivates a previously deactivated station.
    [HttpPut("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var success = await _stationService.ReactivateAsync(id);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}
