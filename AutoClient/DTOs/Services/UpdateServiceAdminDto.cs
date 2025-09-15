namespace AutoClient.DTOs.Services;

public class UpdateServiceAdminDto
{
    public DateTime? EntryDate { get; set; }
    public DateTime? ExitDate { get; set; }      
    public int? Mileage { get; set; } 
    public string? ServiceType { get; set; }
    public string? Description { get; set; }
    public string? MechanicNotes { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public string? NextServiceMileageTarget { get; set; }
    public decimal? Cost { get; set; }
    public Guid? WorkerId { get; set; } 
}
