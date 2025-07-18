namespace AutoClient.DTOs.Clients;

public class UpdateClientDto
{
    public string Name { get; set; }
    public string Phone { get; set; }
    public string DNI { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}
