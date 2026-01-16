using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class WorkshopNotificationSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public Guid WorkshopId { get; set; }

    /// <summary>Enable automatic email when service is marked as delivered/completed</summary>
    public bool VehicleDeliveredEnabled { get; set; } = false;

    /// <summary>Template to use for delivery notification (default: CarReady)</summary>
    [MaxLength(50)]
    public string VehicleDeliveredTemplate { get; set; } = "CarReady";

    /// <summary>Only send if client has email on file (skip if missing)</summary>
    public bool OnlyIfEmailExists { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    [ForeignKey("WorkshopId")]
    public Workshop? Workshop { get; set; }
}
