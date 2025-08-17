using System.Globalization;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using AutoClient.Settings;

namespace AutoClient.Services.Email
{
    /// <summary>
    /// Implementación con MailKit/MimeKit para enviar el correo de factura.
    /// </summary>
    public sealed class InvoiceMailer : IInvoiceMailer
    {
        private readonly SmtpSettings _smtp;

        public InvoiceMailer(IOptions<SmtpSettings> smtpOpt)
            => _smtp = smtpOpt.Value;

        public async Task SendAsync(InvoiceEmailView inv, byte[] pdfBytes, bool sendEmail, CancellationToken ct = default)
        {
            if (!sendEmail) return;
            if (string.IsNullOrWhiteSpace(inv.ClientEmail)) return;

            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(_smtp.SenderName, _smtp.SenderEmail));
            msg.To.Add(new MailboxAddress(inv.ClientName ?? "", inv.ClientEmail));
            // Copia interna
            msg.Bcc.Add(MailboxAddress.Parse("autoserviciosdiogenes@gmail.com"));
            msg.Subject = $"Factura {inv.InvoiceNumber} - Auto Servicios Diógenes";

            var builder = new BodyBuilder();

            // Usa URL pública del logo (simple y compatible). Si prefieres CID, puedes adaptarlo.
            var logoUrl = "https://github.com/Grupo-Geshk/AutoClient-front/blob/main/public/ASD.jpeg?raw=true";

            builder.HtmlBody = BuildHtml(inv, logoUrl);
            builder.TextBody = BuildText(inv);

            if (pdfBytes?.Length > 0)
                builder.Attachments.Add($"Factura_{inv.InvoiceNumber}.pdf", pdfBytes, new ContentType("application", "pdf"));

            msg.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.StartTls, ct);
            if (!string.IsNullOrEmpty(_smtp.Username))
                await smtp.AuthenticateAsync(_smtp.Username, _smtp.Password, ct);

            await smtp.SendAsync(msg, ct);
            await smtp.DisconnectAsync(true, ct);
        }

        private static string Money(decimal v)
            => "$ " + v.ToString("N2", CultureInfo.InvariantCulture);

        private static string BuildHtml(InvoiceEmailView inv, string logoUrl)
        {
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
          <div class='title'>AUTO SERVICIOS DIÓGENES</div>
          <div class='muted'>R.U.C. 4-248-714 D.V. 18</div>
          <div class='tiny muted'>Ventas al por menor de partes, piezas y accesorios de vehículos y automotores</div>
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
    </div>

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
            var sb = new StringBuilder();
            sb.AppendLine($"AUTO SERVICIOS DIÓGENES");
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
            return sb.ToString();
        }
    }
}
