using System.Threading;
using System.Threading.Tasks;

namespace AutoClient.Services.Email
{
    /// <summary>
    /// Envía el correo de factura con HTML “estilo talonario” y adjunta el PDF.
    /// </summary>
    public interface IInvoiceMailer
    {
        Task SendAsync(InvoiceEmailView inv, byte[] pdfBytes, bool sendEmail, CancellationToken ct = default);
    }
}
