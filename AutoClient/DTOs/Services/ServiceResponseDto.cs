namespace AutoClient.DTOs.Services;

public class ServiceResponseDto
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTime EntryDate { get; set; }
    public string WorkerName { get; set; }
    public string Estado => ExitDate == null ? "Pendiente" : "Completado";
    public string? HaceCuantosDias => ExitDate.HasValue
    ? $"{(DateTime.UtcNow - ExitDate.Value).Days} días"
    : null;
    public int Mileage { get; set; }
    public string? ServiceType { get; set; }
    public string? Description { get; set; }
    public string? MechanicNotes { get; set; }
    public string PlateNumber { get; set; } = default!;
    public string ClientName { get; set; } = default!;
    public string Brand { get; set; }
    public string Model { get; set; }
    public int Year { get; set; }
    public DateTime? ExitDate { get; set; }
    
}
