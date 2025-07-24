namespace AutoClient.DTOs.Services;

public class ServiceResponseDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTime EntryDate { get; set; }
    public int Mileage { get; set; }
    public string? ServiceType { get; set; }
    public string? Description { get; set; }
    public string? MechanicNotes { get; set; }
    public string PlateNumber { get; set; } = default!;
    public string ClientName { get; set; } = default!;
    public DateTime? ExitDate { get; set; }
}
