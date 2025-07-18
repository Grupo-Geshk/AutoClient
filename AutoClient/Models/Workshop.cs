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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Client> Clients { get; set; }
}
