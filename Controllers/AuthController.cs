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

    // Handles POST /api/auth/login — validates credentials and returns JWT + user info.
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var (response, accountInactive) = await _authService.LoginAsync(request);
        if (accountInactive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Account is pending approval or has been deactivated. Please contact the Backoffice.",
            });
        }

        if (response is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(response);
    }

    // Handles GET /api/auth/me — returns the current authenticated user's info from JWT claims.
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);
        var name = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);

        return Ok(new { id, name, email, role });
    }
}
