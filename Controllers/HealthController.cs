/**
 * File: HealthController.cs
 * Purpose: Anonymous database-backed health endpoint for IIS and LAN reachability checks.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly IMongoDbService _db;
    private readonly ILogger<HealthController> _logger;

    // Initializes the health endpoint with database access and failure logging.
    public HealthController(IMongoDbService db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Returns healthy only when MongoDB responds to a ping command.
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            await _db.PingAsync();
            return Ok(new { status = "ok", database = "ok" });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB health ping failed");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable", database = "unreachable" });
        }
    }
}
