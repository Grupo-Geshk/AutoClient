using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class Invoice
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ServiceId { get; set; } 
    public long InvoiceNumber { get; set; }

    public DateOnly InvoiceDate { get; set; }

    // Cliente
    [MaxLength(200)] public string ClientName { get; set; } = "";
    [MaxLength(200)] public string ClientEmail { get; set; } = "";
    [MaxLength(300)] public string ClientAddress { get; set; } = "";

    // Pago
    [MaxLength(20)] public string PaymentType { get; set; } = "contado"; // contado|credito
    [MaxLength(120)] public string ReceivedBy { get; set; } = "";

    // Totales
    [Column(TypeName = "numeric(12,2)")] public decimal Subtotal { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal Tax { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal Total { get; set; }

    // Archivo
    [MaxLength(500)] public string PdfUrl { get; set; } = "";

    // Auditoría
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedBy { get; set; } = ""; // opcional: UserId

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}
