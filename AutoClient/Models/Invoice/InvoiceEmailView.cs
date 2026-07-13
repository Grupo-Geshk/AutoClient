using System.Collections.Generic;

namespace AutoClient.Services.Email
{
    /// <summary>
    /// View-model para renderizar el HTML del correo (no entidad de BD).
    /// </summary>
    public sealed class InvoiceEmailView
    {
        // Identidad del taller emisor (del perfil); null = encabezado legado
        public string? WorkshopName { get; set; }
        public string? WorkshopRuc { get; set; }
        public string? WorkshopDv { get; set; }
        public string? WorkshopDescription { get; set; }
        public string? WorkshopLogo { get; set; }
        public string? WorkshopNotificationEmail { get; set; }

        public string? InvoiceNumber { get; set; }

        public string? ClientName { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientAddress { get; set; }

        public int Day { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }

        /// <summary>"contado" | "credito"</summary>
        public string? PaymentType { get; set; }
        public string? ReceivedBy { get; set; }

        /// <summary>Notas para el cliente (opcional).</summary>
        public string? Notes { get; set; }

        /// <summary>0.07m para 7%</summary>
        public decimal TaxRate { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        public List<InvoiceEmailItem> Items { get; set; } = new();
    }

    public sealed class InvoiceEmailItem
    {
        public decimal Qty { get; set; }
        public string? Description { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
