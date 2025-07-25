﻿namespace AutoClient.DTOs.Vehicles;

public class VehicleDetailDto
{
    public Guid Id { get; set; }
    public string PlateNumber { get; set; }
    public string Brand { get; set; }
    public string Model { get; set; }
    public int Year { get; set; }
    public string Color { get; set; }
    public string? VIN { get; set; }
    public int? MileageAtRegistration { get; set; }
    public string ClientName { get; set; } = default!;
}
