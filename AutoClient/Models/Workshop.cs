using System.ComponentModel.DataAnnotations;

namespace AutoClient.Models;

public class Workshop
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(100)]
    public string WorkshopName { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; }

    [Required]
    [MaxLength(100)]
    public string Email { get; set; }

    [MaxLength(20)]
    public string Phone { get; set; }

    [MaxLength(100)]
    public string Subdomain { get; set; }

    [Required]
    public string PasswordHash { get; set; }

    // Perfil público / fiscal del taller
    [MaxLength(30)]
    public string? Ruc { get; set; }

    [MaxLength(10)]
    public string? Dv { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(1000)]
    public string? BusinessDescription { get; set; }

    [MaxLength(500)]
    public string? Logo { get; set; }

    [MaxLength(100)]
    public string? NotificationEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Client> Clients { get; set; }
    public ICollection<Worker> Workers { get; set; }
}
