using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class InvoiceItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    public Guid InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = default!;

    [Column(TypeName = "numeric(12,2)")] public decimal Quantity { get; set; }
    [MaxLength(500)] public string Description { get; set; } = "";
    [Column(TypeName = "numeric(12,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal LineTotal { get; set; }
    public int SortOrder { get; set; }
}
