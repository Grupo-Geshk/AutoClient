using AutoClient.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AutoClient.Services.Email;

public class ClientMailer : IClientMailer
{
    private readonly SmtpSettings _cfg;
    private readonly IEmailTemplateRenderer _templateRenderer;

    public ClientMailer(IOptions<SmtpSettings> cfg, IEmailTemplateRenderer templateRenderer)
    {
        _cfg = cfg.Value;
        _templateRenderer = templateRenderer;
    }

    public Task SendOtpAsync(string to, string workshopName, string code, CancellationToken ct = default)
    {
        var subject = $"Código de verificación - {workshopName}";
        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px'>
            <h2>Código de verificación</h2>
            <p>Tu código para <b>{workshopName}</b> es: 
              <b style='font-size:18px; letter-spacing:2px'>{code}</b></p>
            <p>Este código expira en 10 minutos.</p>
          </div>";
        return SendAsync(to, subject, html, ct);
    }

    public Task SendServiceCompletedAsync(
        string to, string clientName, string plate, DateTime completedAt, decimal? cost, CancellationToken ct = default)
    {
        var subject = "Tu auto está listo – AutoClient";
        var dateStr = completedAt.ToString("yyyy-MM-dd HH:mm");
        var price = cost.HasValue ? cost.Value.ToString("C2") : "—";
        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px'>
            <h2>¡Gracias por confiar en AutoClient!</h2>
            <p>Hola {clientName},</p>
            <p>Tu vehículo con placa <b>{plate}</b> está <b>listo para entrega</b> (completado el {dateStr}).</p>
            <p>Costo del servicio: <b>{price}</b></p>
            <p>¡Te esperamos!</p>
          </div>";
        return SendAsync(to, subject, html, ct);
    }

    public Task SendUpcomingServiceReminderAsync(
        string to, string clientName, string plate, DateTime nextDate, string? nextMileageTarget, CancellationToken ct = default)
    {
        var subject = "Recordatorio: próximo servicio – AutoClient";
        var dateStr = nextDate.ToString("yyyy-MM-dd");
        var mileage = string.IsNullOrWhiteSpace(nextMileageTarget) ? "" : $" o al alcanzar <b>{nextMileageTarget}</b> km";
        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px'>
            <h2>Recordatorio de próximo servicio</h2>
            <p>Hola {clientName},</p>
            <p>Tu vehículo con placa <b>{plate}</b> tiene programado el próximo servicio para el <b>{dateStr}</b>{mileage}.</p>
            <p>Agenda tu cita para evitar contratiempos.</p>
          </div>";
        return SendAsync(to, subject, html, ct);
    }

    public Task SendTemplateAsync(string to, EmailTemplateType templateType, EmailTemplateModel model, CancellationToken ct = default)
    {
        var (subject, htmlBody) = _templateRenderer.Render(templateType, model);
        return SendAsync(to, subject, htmlBody, ct);
    }

    private async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var msg = new MimeMessage();
        msg.From.Add(new MailboxAddress(_cfg.SenderName, _cfg.SenderEmail));
        msg.To.Add(MailboxAddress.Parse(to));
        msg.Subject = subject;
        msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        // Elige automáticamente el modo seguro correcto según el puerto
        var secure = _cfg.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_cfg.Host, _cfg.Port, secure, ct);
        await smtp.AuthenticateAsync(_cfg.Username, _cfg.Password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}
