namespace AutoClient.DTOs.Auth;

public class RegisterWorkshopDto
{
    public string WorkshopName { get; set; }
    public string Username { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public string Subdomain { get; set; }
    public string Password { get; set; }
}
