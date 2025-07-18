namespace AutoClient.DTOs.Vehicles;

public class CreateVehicleDto
{
    public Guid ClientId { get; set; }
    public string PlateNumber { get; set; }
    public string Brand { get; set; }
    public string Model { get; set; }
    public int Year { get; set; }
    public string Color { get; set; }
    public string? VIN { get; set; }
    public int? MileageAtRegistration { get; set; }
}
