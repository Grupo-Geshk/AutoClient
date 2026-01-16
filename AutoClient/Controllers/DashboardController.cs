using AutoClient.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Returns a complete dashboard summary for the specified date range
    /// </summary>
    /// <param name="from">Start date of the range (ISO format)</param>
    /// <param name="to">End date of the range (ISO format)</param>
    /// <param name="workerId">Optional worker ID to filter by specific worker</param>
    /// <returns>Dashboard summary with aggregated metrics</returns>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AutoClient.DTOs.Dashboard.DashboardSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] Guid? workerId = null)
    {
        // Validate date range
        if (from > to)
        {
            return BadRequest(new { message = "Start date must be before end date" });
        }

        // Validate date range is not too large (prevent performance issues)
        if ((to - from).TotalDays > 365)
        {
            return BadRequest(new { message = "Date range cannot exceed 365 days" });
        }

        var workshopId = GetWorkshopId();
        var summary = await _dashboardService.GetSummaryAsync(
            workshopId,
            from.UtcDateTime,
            to.UtcDateTime,
            workerId);

        return Ok(summary);
    }

    private Guid GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        if (claim == null)
        {
            throw new UnauthorizedAccessException("Workshop ID not found in token");
        }
        return Guid.Parse(claim);
    }
}
