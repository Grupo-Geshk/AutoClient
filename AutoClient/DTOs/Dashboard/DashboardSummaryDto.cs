namespace AutoClient.DTOs.Dashboard;

/// <summary>
/// Represents a complete dashboard summary for a workshop
/// </summary>
public class DashboardSummaryDto
{
    // Date range for the summary
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    // Service counts
    public int CompletedCount { get; set; }
    public int PendingCount { get; set; }
    public int InProgressCount { get; set; }
    public int OverdueCount { get; set; }

    // Financial metrics
    public decimal TotalRevenue { get; set; }
    public decimal AverageTicketValue { get; set; }

    // Time-based metrics
    public double AverageDaysToComplete { get; set; }

    // Top worker information
    public string? TopWorkerName { get; set; }
    public int TopWorkerServiceCount { get; set; }

    // Top services
    public List<ServiceTypeCount> TopServices { get; set; } = new();

    // Services requiring attention
    public List<NextActionDto> NextActions { get; set; } = new();
}

/// <summary>
/// Service type count for top services
/// </summary>
public class ServiceTypeCount
{
    public string ServiceType { get; set; } = string.Empty;
    public int Count { get; set; }
}
