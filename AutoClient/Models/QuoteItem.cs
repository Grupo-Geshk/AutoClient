using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoClient.Models;

public class QuoteItem
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [Required] public Guid QuoteId { get; set; }
    [ForeignKey(nameof(QuoteId))] public Quote? Quote { get; set; }

    public int Position { get; set; }

    [Required, MaxLength(500)] public string Description { get; set; } = "";

    [Column(TypeName = "numeric(12,2)")] public decimal Quantity { get; set; } = 1;
    [Column(TypeName = "numeric(12,2)")] public decimal UnitPrice { get; set; }
    [Column(TypeName = "numeric(12,2)")] public decimal LineTotal { get; set; }
}
