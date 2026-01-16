namespace AutoClient.DTOs.Dashboard;

/// <summary>
/// Represents a service requiring immediate attention in the dashboard
/// </summary>
public class NextActionDto
{
    public Guid ServiceId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DaysOpen { get; set; }
    public DateTime EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
}
