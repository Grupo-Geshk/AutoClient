namespace AutoClient.DTOs
{
    public class InvoiceRequestDto
    {
        public string Title { get; set; } = "Factura Electrónica";
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string Description { get; set; } = "Servicio prestado";
        public decimal Amount { get; set; }
    }
}
