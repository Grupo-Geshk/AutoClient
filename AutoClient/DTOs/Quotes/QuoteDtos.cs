namespace AutoClient.DTOs.Quotes;

public class QuoteItemInputDto
{
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
}

public class QuoteCreateDto
{
    public string ClientName { get; set; } = "";
    public string? ClientEmail { get; set; }
    public string? ClientPhone { get; set; }
    public string? VehicleInfo { get; set; }
    public string? Notes { get; set; }
    public DateOnly? ValidUntil { get; set; }
    /// <summary>Tasa de impuesto en porcentaje (ej. 7 para ITBMS 7%). 0 = sin impuesto.</summary>
    public decimal TaxRate { get; set; } = 0;
    public List<QuoteItemInputDto> Items { get; set; } = new();
}

public class QuoteItemDto
{
    public Guid Id { get; set; }
    public int Position { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class QuoteDto
{
    public Guid Id { get; set; }
    public long QuoteNumber { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string ClientName { get; set; } = "";
    public string ClientEmail { get; set; } = "";
    public string ClientPhone { get; set; } = "";
    public string VehicleInfo { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string ShareToken { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public List<QuoteItemDto> Items { get; set; } = new();
}

public class QuoteSummaryDto
{
    public Guid Id { get; set; }
    public long QuoteNumber { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string ClientName { get; set; } = "";
    public string VehicleInfo { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string ShareToken { get; set; } = "";
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Vista pública: sin datos internos de administración.</summary>
public class PublicQuoteDto
{
    public long QuoteNumber { get; set; }
    public DateOnly QuoteDate { get; set; }
    public DateOnly? ValidUntil { get; set; }
    public string ClientName { get; set; } = "";
    public string VehicleInfo { get; set; } = "";
    public string Notes { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
    public List<QuoteItemDto> Items { get; set; } = new();

    // Datos públicos del taller para el encabezado del documento
    public string WorkshopName { get; set; } = "";
    public string? WorkshopLogo { get; set; }
    public string? WorkshopPhone { get; set; }
    public string? WorkshopAddress { get; set; }
    public string? WorkshopEmail { get; set; }
    public string? WorkshopRuc { get; set; }
    public string? WorkshopDv { get; set; }
}
