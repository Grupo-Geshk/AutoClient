using System.Globalization;
using System.Text;
using AutoClient.Services.Email;

namespace AutoClient.Services.Email
{
    /// <summary>
    /// Implementation using IEmailSender to send invoice emails via Resend API.
    /// </summary>
    public sealed class InvoiceMailer : IInvoiceMailer
    {
        private readonly IEmailSender _emailSender;

        public InvoiceMailer(IEmailSender emailSender)
            => _emailSender = emailSender;

        public async Task SendAsync(InvoiceEmailView inv, byte[] pdfBytes, bool sendEmail, CancellationToken ct = default)
        {
            if (!sendEmail) return;
            if (string.IsNullOrWhiteSpace(inv.ClientEmail)) return;

            var workshopName = string.IsNullOrWhiteSpace(inv.WorkshopName)
                ? "Auto Servicios Diógenes"
                : inv.WorkshopName;

            var subject = $"Factura {inv.InvoiceNumber} - {workshopName}";

            // Logo del perfil del taller, o el legado si no hay
            var logoUrl = string.IsNullOrWhiteSpace(inv.WorkshopLogo)
                ? "https://github.com/Grupo-Geshk/AutoClient-front/blob/main/public/ASD.jpeg?raw=true"
                : inv.WorkshopLogo;

            var htmlBody = BuildHtml(inv, logoUrl);
            var textBody = BuildText(inv);

            // Prepare attachment if PDF provided
            var attachments = new List<EmailAttachment>();
            if (pdfBytes?.Length > 0)
            {
                attachments.Add(new EmailAttachment
                {
                    FileName = $"Factura_{inv.InvoiceNumber}.pdf",
                    Content = pdfBytes,
                    ContentType = "application/pdf"
                });
            }

            // Copia interna: email de notificaciones del perfil, o el legado
            var bccList = new[]
            {
                string.IsNullOrWhiteSpace(inv.WorkshopNotificationEmail)
                    ? "autoserviciosdiogenes@gmail.com"
                    : inv.WorkshopNotificationEmail
            };

            await _emailSender.SendAsync(
                to: inv.ClientEmail,
                subject: subject,
                htmlBody: htmlBody,
                textBody: textBody,
                attachments: attachments,
                bcc: bccList,
                ct: ct);
        }

        private static string Money(decimal v)
            => "$ " + v.ToString("N2", CultureInfo.InvariantCulture);

        private static (string Name, string RucLine, string Description) ResolveBrand(InvoiceEmailView inv)
        {
            // Sin taller asociado: identidad legada
            if (string.IsNullOrWhiteSpace(inv.WorkshopName))
                return (
                    "AUTO SERVICIOS DIÓGENES",
                    "R.U.C. 4-248-714 D.V. 18",
                    "Ventas al por menor de partes, piezas y accesorios de vehículos y automotores");

            var rucLine = string.IsNullOrWhiteSpace(inv.WorkshopRuc)
                ? ""
                : $"R.U.C. {inv.WorkshopRuc}" +
                  (string.IsNullOrWhiteSpace(inv.WorkshopDv) ? "" : $" D.V. {inv.WorkshopDv}");

            return (inv.WorkshopName.ToUpperInvariant(), rucLine, inv.WorkshopDescription ?? "");
        }

        private static string BuildHtml(InvoiceEmailView inv, string logoUrl)
        {
            var (brandName, rucLine, description) = ResolveBrand(inv);
            var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
            var rucHtml = string.IsNullOrWhiteSpace(rucLine)
                ? ""
                : $"<div class='muted'>{enc(rucLine)}</div>";
            var descHtml = string.IsNullOrWhiteSpace(description)
                ? ""
                : $"<div class='tiny muted'>{enc(description)}</div>";

            var sb = new StringBuilder();
            sb.Append($@"
<!DOCTYPE html>
<html lang='es'>
<head>
<meta charset='utf-8' />
<meta name='viewport' content='width=device-width, initial-scale=1' />
<title>Factura {inv.InvoiceNumber}</title>
<style>
  body {{ font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, 'Helvetica Neue', Arial; background:#f7f7f9; margin:0; padding:24px; }}
  .card {{ max-width:760px; margin:0 auto; background:#fff; border:1px solid #e5e7eb; border-radius:16px; padding:20px 24px; }}
  .row {{ display:flex; align-items:center; justify-content:space-between; gap:16px; }}
  .muted {{ color:#6b7280; font-size:12px; line-height:1.2; }}
  .title {{ font-weight:700; letter-spacing:.3px; }}
  .tiny {{ font-size:11px; }}
  .table {{ width:100%; border-collapse:collapse; margin-top:12px; }}
  .table th, .table td {{ border:1px solid #e5e7eb; padding:8px; font-size:13px; }}
  .table th {{ background:#fafafa; text-align:center; }}
  .right {{ text-align:right; }}
  .totals {{ width:280px; margin-left:auto; font-size:14px; }}
  .totals td {{ padding:6px 8px; }}
  .badge {{ display:inline-block; font-size:12px; border:1px solid #e5e7eb; padding:2px 6px; border-radius:6px; }}
  .footer {{ margin-top:16px; font-size:12px; color:#6b7280; }}
</style>
</head>
<body>
  <div class='card'>
    <div class='row' style='border-bottom:1px solid #e5e7eb; padding-bottom:10px;'>
      <div class='row' style='gap:12px;'>
        <img src='{logoUrl}' alt='Logo' style='height:48px; width:48px; object-fit:contain;' />
        <div>
          <div class='title'>{enc(brandName)}</div>
          {rucHtml}
          {descHtml}
        </div>
      </div>

      <div style='text-align:right'>
        <div style='font-weight:600'>FACTURA</div>
        <div style='margin-top:6px; display:grid; grid-template-columns:repeat(3,64px); gap:4px; font-size:11px;'>
          <div style='text-align:center;'>
            <div class='badge'>DÍA</div>
            <div style='border:1px solid #e5e7eb; padding:2px;'>{inv.Day}</div>
          </div>
          <div style='text-align:center;'>
            <div class='badge'>MES</div>
            <div style='border:1px solid #e5e7eb; padding:2px;'>{inv.Month}</div>
          </div>
          <div style='text-align:center;'>
            <div class='badge'>AÑO</div>
            <div style='border:1px solid #e5e7eb; padding:2px;'>{inv.Year}</div>
          </div>
        </div>
        <div style='margin-top:8px; font-size:12px;'>
          <span class='badge'>{(inv.PaymentType?.ToLower() == "credito" ? "CRÉDITO" : "CONTADO")}</span>
        </div>
      </div>
    </div>

    <div style='margin-top:10px; font-size:14px;'>
      <div style='display:flex; align-items:center; gap:8px; margin-bottom:6px;'>
        <span style='width:80px; font-weight:600;'>Cliente:</span>
        <span style='flex:1; border:1px solid #e5e7eb; padding:6px 8px;'>{inv.ClientName}</span>
      </div>
      <div style='display:flex; align-items:center; gap:8px; margin-bottom:6px;'>
        <span style='width:80px; font-weight:600;'>Dirección:</span>
        <span style='flex:1; border:1px solid #e5e7eb; padding:6px 8px;'>{inv.ClientAddress}</span>
      </div>
      <div style='display:flex; align-items:center; gap:8px;'>
        <span style='width:80px; font-weight:600;'>Correo:</span>
        <span style='flex:1; border:1px solid #e5e7eb; padding:6px 8px;'>{inv.ClientEmail}</span>
      </div>
    </div>

    <table class='table'>
      <thead>
        <tr>
          <th style='width:80px;'>CANT.</th>
          <th>DESCRIPCIÓN</th>
          <th style='width:120px;'>P. UNIT.</th>
          <th style='width:120px;'>TOTAL</th>
        </tr>
      </thead>
      <tbody>");
            foreach (var it in inv.Items)
            {
                var line = it.Qty * it.UnitPrice;
                sb.Append($@"
        <tr>
          <td class='right'>{it.Qty:0.##}</td>
          <td>{System.Net.WebUtility.HtmlEncode(it.Description ?? "")}</td>
          <td class='right'>{Money(it.UnitPrice)}</td>
          <td class='right'>{Money(line)}</td>
        </tr>");
            }
            sb.Append($@"
      </tbody>
    </table>

    <div style='display:flex; gap:16px; margin-top:10px;'>
      <div style='flex:1;'>
        <div style='font-size:14px;'><strong>Recibido por:</strong> {inv.ReceivedBy}</div>
      </div>

      <table class='totals'>
        <tr>
          <td class='right'>SUB-TOTAL:</td>
          <td class='right'>{Money(inv.Subtotal)}</td>
        </tr>
        <tr>
          <td class='right'>I.T.B.M.S ({inv.TaxRate:P0}):</td>
          <td class='right'>{Money(inv.Tax)}</td>
        </tr>
        <tr>
          <td class='right' style='font-weight:700;'>TOTAL:</td>
          <td class='right' style='font-weight:700;'>{Money(inv.Total)}</td>
        </tr>
      </table>
    </div>");
            if (!string.IsNullOrWhiteSpace(inv.Notes))
            {
                sb.Append($@"
    <div style='margin-top:12px; font-size:13px;'>
      <div style='font-weight:700; margin-bottom:4px;'>NOTAS</div>
      <div style='border:1px solid #e5e7eb; padding:8px; white-space:pre-wrap;'>{System.Net.WebUtility.HtmlEncode(inv.Notes)}</div>
    </div>");
            }
            sb.Append($@"

    <div class='footer'>
      Factura No. {inv.InvoiceNumber} · {inv.Day:00}/{inv.Month:00}/{inv.Year} · {inv.PaymentType?.ToUpperInvariant()}
    </div>
  </div>
</body>
</html>");
            return sb.ToString();
        }

        private static string BuildText(InvoiceEmailView inv)
        {
            var (brandName, rucLine, _) = ResolveBrand(inv);
            var sb = new StringBuilder();
            sb.AppendLine(brandName);
            if (!string.IsNullOrWhiteSpace(rucLine)) sb.AppendLine(rucLine);
            sb.AppendLine($"Factura {inv.InvoiceNumber} - {inv.Day:00}/{inv.Month:00}/{inv.Year}");
            sb.AppendLine($"Cliente: {inv.ClientName}");
            sb.AppendLine($"Dirección: {inv.ClientAddress}");
            sb.AppendLine($"Correo: {inv.ClientEmail}");
            sb.AppendLine($"Pago: {(inv.PaymentType?.ToLower() == "credito" ? "CRÉDITO" : "CONTADO")}");
            sb.AppendLine();
            foreach (var it in inv.Items)
                sb.AppendLine($"{it.Qty:0.##} x {it.Description} @ {Money(it.UnitPrice)} = {Money(it.Qty * it.UnitPrice)}");
            sb.AppendLine();
            sb.AppendLine($"SUBTOTAL: {Money(inv.Subtotal)}");
            sb.AppendLine($"ITBMS ({inv.TaxRate:P0}): {Money(inv.Tax)}");
            sb.AppendLine($"TOTAL: {Money(inv.Total)}");
            sb.AppendLine();
            sb.AppendLine($"Recibido por: {inv.ReceivedBy}");
            if (!string.IsNullOrWhiteSpace(inv.Notes))
            {
                sb.AppendLine();
                sb.AppendLine("NOTAS:");
                sb.AppendLine(inv.Notes);
            }
            return sb.ToString();
        }
    }
}
