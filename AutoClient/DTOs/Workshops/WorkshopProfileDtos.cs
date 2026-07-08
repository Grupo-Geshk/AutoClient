namespace AutoClient.DTOs.Workshops;

public class WorkshopProfileDto
{
    public Guid Id { get; set; }
    public string WorkshopName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Subdomain { get; set; }
    public string? Ruc { get; set; }
    public string? Dv { get; set; }
    public string? Address { get; set; }
    public string? BusinessDescription { get; set; }
    public string? Logo { get; set; }
    public string? NotificationEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UpdateWorkshopProfileDto
{
    public string? WorkshopName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Ruc { get; set; }
    public string? Dv { get; set; }
    public string? Address { get; set; }
    public string? BusinessDescription { get; set; }
    public string? Logo { get; set; }
    public string? NotificationEmail { get; set; }
}
