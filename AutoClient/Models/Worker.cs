using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class Worker
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [ForeignKey("Workshop")]
    public Guid WorkshopId { get; set; }
    public Workshop Workshop { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [Required]
    [MaxLength(100)]
    public string Email { get; set; }

    [MaxLength(20)]
    public string Phone { get; set; }

    [MaxLength(100)]
    public string Role { get; set; }  // Ej: Mecánico, Supervisor

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
