using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class ServiceType
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [ForeignKey("Workshop")]
    public Guid WorkshopId { get; set; }
    public Workshop Workshop { get; set; }

    [Required]
    [MaxLength(100)]
    public string ServiceTypeName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
