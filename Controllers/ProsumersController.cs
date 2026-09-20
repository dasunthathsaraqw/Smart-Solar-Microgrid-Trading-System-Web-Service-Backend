/**
 * File: ProsumersController.cs
 * Purpose: Backoffice-only endpoints for prosumer registration, lookup, update and lifecycle actions.
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
[Route("api/prosumers")]
[Authorize(Roles = "Backoffice")]
public class ProsumersController : ControllerBase
{
    private readonly IProsumerService _prosumerService;

    public ProsumersController(IProsumerService prosumerService)
    {
        _prosumerService = prosumerService;
    }

    // Handles GET /api/prosumers?status={active|pending|deactivated} — lists prosumers, optionally filtered by status.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status)
    {
        var prosumers = await _prosumerService.GetAllAsync(status);
        return Ok(prosumers);
    }

    // Handles GET /api/prosumers/pending — convenience shortcut for the pending-approval list.
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending()
    {
        var prosumers = await _prosumerService.GetAllAsync("pending");
        return Ok(prosumers);
    }

    // Handles GET /api/prosumers/{nic} — returns a single prosumer by NIC.
    [HttpGet("{nic}")]
    public async Task<IActionResult> GetByNic(string nic)
    {
        var prosumer = await _prosumerService.GetByNicAsync(nic);
        if (prosumer is null)
        {
            return NotFound();
        }

        return Ok(prosumer);
    }

    // Handles POST /api/prosumers — registers a new prosumer, pending Backoffice approval.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProsumerRequest request)
    {
        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var prosumer = await _prosumerService.CreateAsync(request, createdBy);
            return CreatedAtAction(nameof(GetByNic), new { nic = prosumer.Nic }, prosumer);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // Handles PUT /api/prosumers/{nic} — updates the editable fields of a prosumer.
    [HttpPut("{nic}")]
    public async Task<IActionResult> Update(string nic, [FromBody] UpdateProsumerRequest request)
    {
        try
        {
            var updated = await _prosumerService.UpdateAsync(nic, request);
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

    // Handles PUT /api/prosumers/{nic}/deactivate — marks a prosumer as deactivated.
    [HttpPut("{nic}/deactivate")]
    public async Task<IActionResult> Deactivate(string nic)
    {
        var success = await _prosumerService.DeactivateAsync(nic);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    // Handles PUT /api/prosumers/{nic}/reactivate — approves a pending prosumer or reactivates a deactivated one.
    [HttpPut("{nic}/reactivate")]
    public async Task<IActionResult> Reactivate(string nic)
    {
        var success = await _prosumerService.ReactivateAsync(nic);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}
