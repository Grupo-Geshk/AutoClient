namespace AutoClient.DTOs.Workers;

public class CreateWorkerDto
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string? Phone { get; set; }
    public string? Role { get; set; }
}
