using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class EmailLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int IdLog { get; set; }

    [Required]
    public Guid WorkshopId { get; set; }

    [Required]
    public Guid ClientId { get; set; }

    [Required]
    [MaxLength(255)]
    public string CorreoDestino { get; set; } = "";

    [Required]
    [MaxLength(50)]
    public string TipoEnvio { get; set; } = "";

    [Required]
    public DateTime FechaEnvio { get; set; } = DateTime.UtcNow;

    [Required]
    public bool Estado { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }
}
