/**
 * File: ProsumerReportsController.cs
 * Purpose: Prosumer-only live dashboard endpoint scoped to the signed NIC claim.
 * Author: M.K.E Dharmarathne it23142732
 * Date: 2026
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMicrogrid.API.Services;

namespace SmartMicrogrid.API.Controllers;

// Separate controller avoids AND-combining Prosumer access with ReportsController's management-only class gate.
[ApiController]
[Route("api/reports")]
public class ProsumerReportsController : ControllerBase
{
    private readonly IReportService _reportService;

    // Initializes the Prosumer dashboard endpoint with live report queries.
    public ProsumerReportsController(IReportService reportService)
    {
        _reportService = reportService;
    }

    // Handles GET /api/reports/my-dashboard using only the NIC signed into the access token.
    [HttpGet("my-dashboard")]
    [Authorize(Roles = "Prosumer")]
    public async Task<IActionResult> GetMyDashboard()
    {
        // The NIC must come from the signed token, never client-controlled route or body data.
        var nic = User.FindFirst("nic")?.Value;
        if (string.IsNullOrWhiteSpace(nic))
        {
            // This controller uses the { error } shape, while the profile endpoints use { message }.
            return Unauthorized(new { error ="The access token does not contain a NIC claim." });
        }

        var dashboard = await _reportService.GetProsumerDashboardAsync(nic);
        return Ok(dashboard);
    }
}
