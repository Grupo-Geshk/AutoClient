using AutoClient.Models;
using System.ComponentModel.DataAnnotations;

public class LoginOtp
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkshopId { get; set; }
    public Workshop Workshop { get; set; }

    [Required, MaxLength(128)]
    public string CodeHash { get; set; } // hash del código

    [Required, MaxLength(128)]
    public string OtpToken { get; set; } // token de correlación

    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;
}
