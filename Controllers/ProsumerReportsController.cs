/**
 * File: ProsumerReportsController.cs
 * Purpose: Prosumer-only live dashboard endpoint scoped to the signed NIC claim.
 * Author: AUTHOR_NAME AUTHOR_IT_NUMBER
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
        var nic = User.FindFirst("nic")?.Value;
        if (string.IsNullOrWhiteSpace(nic))
        {
            return Unauthorized(new { error = "The access token does not contain a NIC claim." });
        }

        var dashboard = await _reportService.GetProsumerDashboardAsync(nic);
        return Ok(dashboard);
    }
}
