/**
 * File: StationsController.cs
 * Purpose: Role-aware station reads and Backoffice-only station management endpoints.
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
[Route("api/stations")]
[Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
public class StationsController : ControllerBase
{
    private readonly IStationService _stationService;

    // Initializes station endpoints with the station business service.
    public StationsController(IStationService stationService)
    {
        _stationService = stationService;
    }

    // Handles GET /api/stations?status={active|deactivated} — lists stations, optionally filtered by status.
    [HttpGet]
    [Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        // Mobile roles must never discover decommissioned nodes, even through a crafted status query.
        var effectiveStatus = User.IsInRole("Backoffice") ? status : "active";
        var stations = await _stationService.GetAllAsync(effectiveStatus);
        return Ok(stations);
    }

    // Handles GET /api/stations/{id} — returns a single station by id.
    [HttpGet("{id}")]
    [Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
    public async Task<IActionResult> GetById(string id)
    {
        var station = await _stationService.GetByIdAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        // Returning 404 prevents mobile roles from learning that a decommissioned station exists.
        if (!User.IsInRole("Backoffice") && !station.IsActive)
        {
            return NotFound();
        }

        return Ok(station);
    }

    // Handles GET /api/stations/nearby and returns distance-ordered active stations with slot counts.
    // Defaults (10 km radius, 20 results) apply when the app omits them; range checks live in the service and surface as 400.
    [HttpGet("nearby")]
    [Authorize(Roles = "Backoffice,GridOperator,Prosumer")]
    public async Task<IActionResult> GetNearby(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] double radiusKm = 10,
        [FromQuery] int limit = 20)
    {
        try
        {
            var stations = await _stationService.GetNearbyAsync(latitude, longitude, radiusKm, limit);
            return Ok(stations);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Handles POST /api/stations — registers a new microgrid station.
    [HttpPost]
    [Authorize(Roles = "Backoffice")]
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
            // The only failure the service raises here is a duplicate station name, hence 409.
            return Conflict(new { message = ex.Message });
        }
    }

    // Handles PUT /api/stations/{id} — updates the editable fields of a station.
    [HttpPut("{id}")]
    [Authorize(Roles = "Backoffice")]
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
    [Authorize(Roles = "Backoffice")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var (success, error) = await _stationService.DeactivateAsync(id);
        if (!success)
        {
            // The service reports failures as text, so "not found" is recognised by comparing the message: keep it in sync with StationService.
            // Any other failure (an Approved reservation blocking deactivation) is a 400, and uses the { error } shape rather than { message }.
            return error == "Station not found" ? NotFound() : BadRequest(new { error });
        }

        return NoContent();
    }

    // Handles PUT /api/stations/{id}/reactivate — reactivates a previously deactivated station.
    [HttpPut("{id}/reactivate")]
    [Authorize(Roles = "Backoffice")]
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
