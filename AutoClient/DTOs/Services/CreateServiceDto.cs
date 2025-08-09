namespace AutoClient.DTOs.Services;

public class CreateServiceDto
{
    public Guid VehicleId { get; set; }
    public DateTime? EntryDate { get; set; } // editable
    public int Mileage { get; set; }
    public string? ServiceType { get; set; }
    public string? Description { get; set; }
    public string? MechanicNotes { get; set; }
    public Guid WorkerId { get; set; }

}
