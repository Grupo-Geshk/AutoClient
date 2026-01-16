using AutoClient.DTOs.Dashboard;

namespace AutoClient.Services;

/// <summary>
/// Service responsible for aggregating and computing dashboard metrics
/// </summary>
public interface IDashboardService
{
    /// <summary>
    /// Gets a complete dashboard summary for the specified date range and workshop
    /// </summary>
    /// <param name="workshopId">The workshop ID to filter by</param>
    /// <param name="from">Start date of the range</param>
    /// <param name="to">End date of the range</param>
    /// <param name="workerId">Optional worker ID to filter by specific worker</param>
    /// <returns>Dashboard summary with all computed metrics</returns>
    Task<DashboardSummaryDto> GetSummaryAsync(
        Guid workshopId,
        DateTime from,
        DateTime to,
        Guid? workerId = null);
}
