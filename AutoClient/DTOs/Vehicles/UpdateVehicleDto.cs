namespace AutoClient.DTOs.Vehicles;

public class UpdateVehicleDto
{
    public string Brand { get; set; }
    public string PlateNumber { get; set; }
    public string Model { get; set; }
    public int Year { get; set; }
    public string Color { get; set; }
    public string? VIN { get; set; }
    public int? MileageAtRegistration { get; set; }
    public string? ImageUrl { get; set; }
}
