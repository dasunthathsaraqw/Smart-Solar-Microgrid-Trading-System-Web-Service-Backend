/**
 * File: UsersController.cs
 * Purpose: Backoffice-only user management endpoints (create, list, get, update, deactivate,
 *          reactivate). Business rules live in UserService — this controller stays thin.
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
[Route("api/users")]
[Authorize(Roles = "Backoffice")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    // Initializes Backoffice user management with the user service.
    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    // Handles GET /api/users?role={Backoffice|GridOperator}&status={active|deactivated} — lists users with optional filters.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? role, [FromQuery] string? status)
    {
        var users = await _userService.GetAllAsync(role, status);
        return Ok(users);
    }

    // Handles GET /api/users/{id} — returns a single user by id.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(user);
    }

    // Handles POST /api/users — creates a new Backoffice or GridOperator account.
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var createdBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var user = await _userService.CreateAsync(request, createdBy);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // Handles PUT /api/users/{id} — updates name, email, password, role and/or active status.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request)
    {
        var updatedBy = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name ?? "unknown";

        try
        {
            var updated = await _userService.UpdateAsync(id, request, updatedBy);
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

    // Handles PUT /api/users/{id}/deactivate — blocked for self-deactivation or the last active Backoffice.
    [HttpPut("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(string id)
    {
        var requestingUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var (success, error) = await _userService.DeactivateAsync(id, requestingUserId);
        if (!success)
        {
            return error == "User not found" ? NotFound() : BadRequest(new { error });
        }

        return NoContent();
    }

    // Handles PUT /api/users/{id}/reactivate — reactivates a previously deactivated user.
    [HttpPut("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var success = await _userService.ReactivateAsync(id);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }
}
