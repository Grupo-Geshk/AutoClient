using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class Service
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [ForeignKey("Vehicle")]
    public Guid VehicleId { get; set; }
    [Required]
    [ForeignKey("Worker")]
    public Guid? WorkerId { get; set; }
    public Worker? Worker { get; set; } // <= nota el signo de pregunta

    public Vehicle Vehicle { get; set; }

    [Required]
    public DateTime Date { get; set; }
    public DateTime? ExitDate { get; set; }

    [Required]
    [MaxLength(500)]
    public string ServiceType { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public int MileageAtService { get; set; }

    public DateTime? NextServiceDate { get; set; } 

    [MaxLength(20)]
    public string? NextServiceMileageTarget { get; set; }

    public decimal? Cost { get; set; }

    [MaxLength(1000)]
    public string? MechanicNotes { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
