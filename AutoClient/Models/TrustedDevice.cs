using AutoClient.Models;
using System.ComponentModel.DataAnnotations;

public class TrustedDevice
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkshopId { get; set; }
    public Workshop Workshop { get; set; }

    [Required, MaxLength(256)]
    public string DeviceTokenHash { get; set; } // hash del token que se guarda en cookie

    [MaxLength(256)]
    public string UserAgent { get; set; }

    [MaxLength(64)]
    public string IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    public bool IsRevoked { get; set; } = false;
}
