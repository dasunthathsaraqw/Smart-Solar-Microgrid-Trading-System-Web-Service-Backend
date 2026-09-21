/**
 * File: ProsumerSelfServiceController.cs
 * Purpose: Anonymous registration and token-owned profile endpoints for mobile prosumers.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMicrogrid.API.Models;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

[ApiController]
[Route("api/prosumers")]
public class ProsumerSelfServiceController : ControllerBase
{
    private readonly IProsumerService _prosumerService;

    // Initializes the self-service endpoints with the prosumer business service.
    public ProsumerSelfServiceController(IProsumerService prosumerService)
    {
        _prosumerService = prosumerService;
    }

    // Handles anonymous POST /api/prosumers/register and creates a pending profile plus credential account.
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterProsumerRequest request)
    {
        try
        {
            var prosumer = await _prosumerService.RegisterAsync(request);
            return Created($"/api/prosumers/{prosumer.Nic}", prosumer);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Handles GET /api/prosumers/me using only the NIC claim so callers cannot read another profile.
    [Authorize(Roles = "Prosumer")]
    [HttpGet("me")]
    public async Task<IActionResult> GetOwnProfile()
    {
        // The NIC must come from the signed token, never client-controlled route or body data.
        var nic = User.FindFirst("nic")?.Value;
        if (nic is null)
        {
            return Unauthorized(new { message = "The access token does not contain a NIC claim." });
        }

        var prosumer = await _prosumerService.GetByNicAsync(nic);
        return prosumer is null ? NotFound() : Ok(prosumer);
    }

    // Handles PUT /api/prosumers/me and restricts changes to the authenticated prosumer's editable fields.
    [Authorize(Roles = "Prosumer")]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateOwnProfile([FromBody] UpdateOwnProfileRequest request)
    {
        // The NIC must come from the signed token, never client-controlled route or body data.
        var nic = User.FindFirst("nic")?.Value;
        if (nic is null)
        {
            return Unauthorized(new { message = "The access token does not contain a NIC claim." });
        }

        try
        {
            var prosumer = await _prosumerService.UpdateOwnProfileAsync(nic, request);
            return prosumer is null ? NotFound() : Ok(prosumer);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Handles PUT /api/prosumers/me/password and changes both synchronized password hashes.
    [Authorize(Roles = "Prosumer")]
    [HttpPut("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        // The NIC must come from the signed token, never client-controlled route or body data.
        var nic = User.FindFirst("nic")?.Value;
        if (nic is null)
        {
            return Unauthorized(new { message = "The access token does not contain a NIC claim." });
        }

        var changed = await _prosumerService.ChangePasswordAsync(nic, request);
        if (!changed)
        {
            return BadRequest(new { message = "Current password is incorrect." });
        }

        return NoContent();
    }

    // Handles PUT /api/prosumers/me/request-deactivation while preserving Backoffice approval control.
    [Authorize(Roles = "Prosumer")]
    [HttpPut("me/request-deactivation")]
    public async Task<IActionResult> RequestDeactivation()
    {
        // The NIC must come from the signed token, never client-controlled route or body data.
        var nic = User.FindFirst("nic")?.Value;
        if (nic is null)
        {
            return Unauthorized(new { message = "The access token does not contain a NIC claim." });
        }

        var (success, error) = await _prosumerService.RequestDeactivationAsync(nic);
        if (!success)
        {
            return BadRequest(new { message = error });
        }

        return NoContent();
    }
}
