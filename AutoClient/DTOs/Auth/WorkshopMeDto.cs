namespace AutoClient.DTOs.Auth;

public class WorkshopMeDto
{
    public Guid Id { get; set; }
    public string WorkshopName { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Subdomain { get; set; }
}
