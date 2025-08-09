// DTOs/Workers/WorkerOverviewDto.cs
namespace AutoClient.DTOs.Workers;

public class WorkerOverviewDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CompletedServices { get; set; }
}
