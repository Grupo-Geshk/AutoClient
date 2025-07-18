using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class Vehicle
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [ForeignKey("Client")]
    public Guid ClientId { get; set; }
    public Client Client { get; set; }

    [Required]
    [MaxLength(20)]
    public string PlateNumber { get; set; }

    [Required]
    [MaxLength(50)]
    public string Brand { get; set; }

    [Required]
    [MaxLength(50)]
    public string Model { get; set; }

    public int Year { get; set; }

    [MaxLength(30)]
    public string Color { get; set; }

    [MaxLength(50)]
    public string? VIN { get; set; }

    public int? MileageAtRegistration { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Service> Services { get; set; }
}
