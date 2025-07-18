namespace AutoClient.DTOs.Vehicles
{
    public class VehicleDto
    {
        public Guid Id { get; set; }
        public Guid ClientId { get; set; }
        public string PlateNumber { get; set; }
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public string? Color { get; set; }
        public int? Year { get; set; }
    }
}
