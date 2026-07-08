using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class Quote
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [Required] public Guid WorkshopId { get; set; }
    [ForeignKey(nameof(WorkshopId))] public Workshop? Workshop { get; set; }

    public long QuoteNumber { get; set; }

    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }

    // Cliente (denormalizado: la cotización es un documento, no exige cliente registrado)
    [MaxLength(200)] public string ClientName { get; set; } = "";
    [MaxLength(200)] public string ClientEmail { get; set; } = "";
    [MaxLength(30)] public string ClientPhone { get; set; } = "";

    // Vehículo en texto libre (marca / modelo / placa)
    [MaxLength(300)] public string VehicleInfo { get; set; } = "";

    [MaxLength(2000)] public string Notes { get; set; } = "";

    // pending | approved | rejected
    [MaxLength(20)] public string Status { get; set; } = "pending";

    // Token de la vista pública inmutable (link compartible)
    [Required, MaxLength(64)] public string ShareToken { get; set; } = "";

    [Column(TypeName = "numeric(12,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal Tax { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal Total { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    public ICollection<QuoteItem> Items { get; set; } = new List<QuoteItem>();
}
