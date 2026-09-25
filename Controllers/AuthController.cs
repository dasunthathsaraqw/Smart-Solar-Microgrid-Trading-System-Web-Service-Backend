/**
 * File: AuthController.cs
 * Purpose: Handles authentication endpoints (login, current user).
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
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    // Initializes the controller with the authentication service.
    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>Authenticates an account and returns its JWT and current server-side identity context.</summary>
    /// <remarks>
    /// A successful GridOperator response includes the nullable persisted stationId. Invalid credentials return
    /// 401; pending, inactive, or deactivated accounts return 403. Validation failures return 400.
    /// </remarks>
    /// <param name="request">Email address and password.</param>
    /// <response code="200">Credentials accepted; returns token, identity, role, station assignment, and expiry.</response>
    /// <response code="400">The request body fails validation.</response>
    /// <response code="401">The credentials are invalid.</response>
    /// <response code="403">The account is pending approval, inactive, or deactivated.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // The service returns (response, accountInactive) so the three outcomes stay distinct: success, bad credentials (401) and inactive account (403).
        var (response, accountInactive) = await _authService.LoginAsync(request);
        if (accountInactive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Account is pending approval or has been deactivated. Please contact the Backoffice.",
            });
        }

        // One generic message for both an unknown email and a wrong password, so the API does not confirm which emails are registered.
        if (response is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(response);
    }

    /// <summary>Returns the authenticated account's latest persisted identity and station context.</summary>
    /// <remarks>
    /// The server reloads the user by the signed NameIdentifier claim. For GridOperators, stationId comes from
    /// persisted User data rather than client input or a JWT station claim, so assignment changes are immediately
    /// visible. An unassigned GridOperator receives a null stationId.
    /// </remarks>
    /// <response code="200">Returns id, name, email, role, and nullable stationId.</response>
    /// <response code="401">The token is missing/invalid, lacks a user ID, or references a deleted account.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me()
    {
        // Login errors above use { message }, whereas this endpoint uses { error }. The app's ApiError class normalizes both shapes.
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id))
        {
            return Unauthorized(new { error = "The access token does not contain a user ID claim." });
        }

        // A deleted account gets 401 (not 404) so the app treats it as a signed-out session. This endpoint does not check IsActive.
        var user = await _authService.GetByIdAsync(id);
        if (user is null)
        {
            return Unauthorized(new { error = "The authenticated user account no longer exists." });
        }

        // An anonymous object is returned on purpose: it exposes only these fields, so PasswordHash and audit fields on User can never leak.
        return Ok(new
        {
            user.Id,
            user.Name,
            user.Email,
            user.Role,
            StationId = user.Role == "GridOperator" ? user.StationId : null,
        });
    }
}
