namespace AutoClient.Services.Email;

public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public (string Subject, string HtmlBody) Render(EmailTemplateType templateType, EmailTemplateModel model)
    {
        return templateType switch
        {
            EmailTemplateType.CarReady => RenderCarReady(model),
            EmailTemplateType.UpcomingVisit => RenderUpcomingVisit(model),
            EmailTemplateType.PartsNeeded => RenderPartsNeeded(model),
            _ => throw new ArgumentException($"Unknown template type: {templateType}", nameof(templateType))
        };
    }

    private (string Subject, string HtmlBody) RenderCarReady(EmailTemplateModel model)
    {
        var subject = "Tu auto está listo – AutoClient";

        var dateStr = model.ServiceDate?.ToString("yyyy-MM-dd HH:mm") ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

        // Only include cost section if cost exists
        var costSection = model.ServiceCost.HasValue
            ? $"<p>Costo del servicio: <b>{model.ServiceCost.Value:C2}</b></p>"
            : "";

        var vehicleInfo = !string.IsNullOrWhiteSpace(model.VehiclePlate)
            ? $"con placa <b>{model.VehiclePlate}</b>"
            : "";

        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px;max-width:600px;margin:0 auto;'>
            <h2 style='color:#2563eb;'>¡Tu vehículo está listo!</h2>
            <p>Hola {model.ClientName},</p>
            <p>¡Buenas noticias! Tu vehículo {vehicleInfo} está <b>listo para entrega</b> (completado el {dateStr}).</p>
            {costSection}
            <hr style='border:none;border-top:1px solid #e5e7eb;margin:20px 0;'>
            <p style='color:#6b7280;font-size:13px;'>
              Si tienes alguna pregunta, contáctanos al <b>{model.WorkshopPhone}</b>.
            </p>
            <p>¡Gracias por confiar en <b>{model.WorkshopName}</b>!</p>
          </div>";

        return (subject, html);
    }

    private (string Subject, string HtmlBody) RenderUpcomingVisit(EmailTemplateModel model)
    {
        var subject = "Recordatorio: próximo servicio – AutoClient";

        var dateStr = model.NextServiceDate?.ToString("yyyy-MM-dd") ?? "pronto";
        var mileage = !string.IsNullOrWhiteSpace(model.NextServiceMileage)
            ? $" o al alcanzar <b>{model.NextServiceMileage}</b> km"
            : "";
        var vehicleInfo = !string.IsNullOrWhiteSpace(model.VehiclePlate)
            ? $"con placa <b>{model.VehiclePlate}</b>"
            : "";

        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px;max-width:600px;margin:0 auto;'>
            <h2 style='color:#2563eb;'>Recordatorio de Servicio</h2>
            <p>Hola {model.ClientName},</p>
            <p>Este es un recordatorio amistoso de que tu vehículo {vehicleInfo} tiene un servicio programado para el <b>{dateStr}</b>{mileage}.</p>
            <p>Te recomendamos agendar tu cita para evitar contratiempos y mantener tu vehículo en óptimas condiciones.</p>
            <hr style='border:none;border-top:1px solid #e5e7eb;margin:20px 0;'>
            <p style='color:#6b7280;font-size:13px;'>
              Para agendar tu cita, llámanos al <b>{model.WorkshopPhone}</b>.
            </p>
            <p>¡Gracias por elegir <b>{model.WorkshopName}</b>!</p>
          </div>";

        return (subject, html);
    }

    private (string Subject, string HtmlBody) RenderPartsNeeded(EmailTemplateModel model)
    {
        var subject = "Repuestos Necesarios para tu Servicio – AutoClient";

        var vehicleInfo = !string.IsNullOrWhiteSpace(model.VehiclePlate)
            ? $"con placa <b>{model.VehiclePlate}</b>"
            : "";
        var partsInfo = !string.IsNullOrWhiteSpace(model.PartsDescription)
            ? $"<p>Repuestos requeridos: <b>{model.PartsDescription}</b></p>"
            : "";

        var html = $@"
          <div style='font-family:Segoe UI,Arial,sans-serif;font-size:14px;max-width:600px;margin:0 auto;'>
            <h2 style='color:#f59e0b;'>Repuestos Necesarios</h2>
            <p>Hola {model.ClientName},</p>
            <p>Actualmente estamos trabajando en tu vehículo {vehicleInfo}, y necesitamos ordenar algunos repuestos antes de poder continuar.</p>
            {partsInfo}
            <p>Te notificaremos tan pronto lleguen los repuestos y podamos reanudar el trabajo. Agradecemos tu paciencia.</p>
            <hr style='border:none;border-top:1px solid #e5e7eb;margin:20px 0;'>
            <p style='color:#6b7280;font-size:13px;'>
              Si tienes alguna pregunta, contáctanos al <b>{model.WorkshopPhone}</b>.
            </p>
            <p>¡Gracias por tu comprensión y por elegir <b>{model.WorkshopName}</b>!</p>
          </div>";

        return (subject, html);
    }
}
