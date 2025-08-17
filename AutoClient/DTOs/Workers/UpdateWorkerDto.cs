namespace AutoClient.DTOs.Workers
{
    // DTO opcional para update (puedes ponerlo en DTOs/Workers/UpdateWorkerDto.cs)
    public class UpdateWorkerDto
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Role { get; set; }
        public string? Cedula { get; set; }
    }
}
