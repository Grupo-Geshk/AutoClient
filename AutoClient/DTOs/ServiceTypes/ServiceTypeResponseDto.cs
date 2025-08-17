namespace AutoClient.DTOs.ServiceTypes;

public class ServiceTypeResponseDto
{
    public Guid Id { get; set; }
    public string ServiceTypeName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
