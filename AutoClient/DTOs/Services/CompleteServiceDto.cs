namespace AutoClient.DTOs.Services;

public class CompleteServiceDto
{
    public DateTime? ExitDate { get; set; }
    public string? FinalObservations { get; set; }
    public string? VehicleState { get; set; }
    public string? DeliveredBy { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public decimal? Cost { get; set; }
}
