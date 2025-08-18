// UpdateClientDto.cs
namespace AutoClient.DTOs.Clients
{
    public class UpdateClientDto
    {
        public string? Name { get; set; }      // <- ahora nullable
        public string? Phone { get; set; }     // <- ahora nullable
        public string? Email { get; set; }     // <- ahora nullable
        public string? Address { get; set; }   // <- ahora nullable
    }
}
