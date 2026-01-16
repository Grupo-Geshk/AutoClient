using AutoClient.Data;
using AutoClient.Models;
using AutoClient.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("notifications")]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IClientMailer _mailer;

    public NotificationsController(ApplicationDbContext context, IClientMailer mailer)
    {
        _context = context;
        _mailer = mailer;
    }

    // ---------------------------
    //  UTIL
    // ---------------------------
    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim!);
    }

    private static int? DaysUntil(DateTime? futureUtcDate)
    {
        if (futureUtcDate == null) return null;
        // Comparación por fecha (no hora) en UTC
        var today = DateTime.UtcNow.Date;
        return (futureUtcDate.Value.Date - today).Days;
    }

    private static HashSet<int> ParseOnlyOn(string? onlyOnCsv)
    {
        if (string.IsNullOrWhiteSpace(onlyOnCsv))
            return new HashSet<int>(new[] { 7, 3, 1, 0 });

        return new HashSet<int>(
            onlyOnCsv.Split(',')
                     .Select(s => s.Trim())
                     .Where(s => int.TryParse(s, out _))
                     .Select(int.Parse)
        );
    }

    // ---------------------------
    //  DTOs
    // ---------------------------
    public class UpcomingPreviewDto
    {
        public Guid ServiceId { get; set; }
        public Guid VehicleId { get; set; }
        public string Plate { get; set; } = "";
        public string? Brand { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }
        public string ClientName { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime NextServiceDate { get; set; }
        public string? NextServiceMileageTarget { get; set; }
        public int DaysLeft { get; set; }
    }

    public class BulkSendRequest
    {
        public List<Guid> Ids { get; set; } = new();
    }

    public class BulkResultDto
    {
        public int Sent { get; set; }
        public int Skipped { get; set; }
        public List<Guid> Errors { get; set; } = new();
    }

    public class SendNotificationRequest
    {
        [Required]
        public Guid ClientId { get; set; }

        [Required]
        public string TemplateType { get; set; } = "";

        public string? EmailOverride { get; set; }

        public Guid? ServiceId { get; set; }

        public string? PartsDescription { get; set; }
    }

    public class EmailLogDto
    {
        public int Id { get; set; }
        public Guid ClientId { get; set; }
        public string? ClientName { get; set; }
        public string Email { get; set; } = "";
        public string TemplateType { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string Status { get; set; } = "";
    }

    public class NotificationSettingsDto
    {
        public bool VehicleDeliveredEnabled { get; set; }
        public string VehicleDeliveredTemplate { get; set; } = "CarReady";
        public bool OnlyIfEmailExists { get; set; }
    }

    // ---------------------------
    //  ENDPOINTS
    // ---------------------------

    /// <summary>Get notification automation settings for the current workshop.</summary>
    [HttpGet("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetNotificationSettings()
    {
        var workshopId = GetCurrentWorkshopId();

        var settings = await _context.WorkshopNotificationSettings
            .FirstOrDefaultAsync(s => s.WorkshopId == workshopId);

        // Return defaults if no settings exist yet
        if (settings == null)
        {
            return Ok(new NotificationSettingsDto
            {
                VehicleDeliveredEnabled = false,
                VehicleDeliveredTemplate = "CarReady",
                OnlyIfEmailExists = true
            });
        }

        return Ok(new NotificationSettingsDto
        {
            VehicleDeliveredEnabled = settings.VehicleDeliveredEnabled,
            VehicleDeliveredTemplate = settings.VehicleDeliveredTemplate,
            OnlyIfEmailExists = settings.OnlyIfEmailExists
        });
    }

    /// <summary>Update notification automation settings for the current workshop.</summary>
    [HttpPut("settings")]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateNotificationSettings([FromBody] NotificationSettingsDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        // Validate template type
        if (!Enum.TryParse<EmailTemplateType>(dto.VehicleDeliveredTemplate, ignoreCase: true, out _))
        {
            return BadRequest(new { message = $"Invalid template type: {dto.VehicleDeliveredTemplate}" });
        }

        var settings = await _context.WorkshopNotificationSettings
            .FirstOrDefaultAsync(s => s.WorkshopId == workshopId);

        if (settings == null)
        {
            // Create new settings
            settings = new WorkshopNotificationSettings
            {
                WorkshopId = workshopId,
                VehicleDeliveredEnabled = dto.VehicleDeliveredEnabled,
                VehicleDeliveredTemplate = dto.VehicleDeliveredTemplate,
                OnlyIfEmailExists = dto.OnlyIfEmailExists,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.WorkshopNotificationSettings.Add(settings);
        }
        else
        {
            // Update existing settings
            settings.VehicleDeliveredEnabled = dto.VehicleDeliveredEnabled;
            settings.VehicleDeliveredTemplate = dto.VehicleDeliveredTemplate;
            settings.OnlyIfEmailExists = dto.OnlyIfEmailExists;
            settings.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return Ok(new NotificationSettingsDto
        {
            VehicleDeliveredEnabled = settings.VehicleDeliveredEnabled,
            VehicleDeliveredTemplate = settings.VehicleDeliveredTemplate,
            OnlyIfEmailExists = settings.OnlyIfEmailExists
        });
    }

    /// <summary>Generic send endpoint for client communication templates.</summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendNotification([FromBody] SendNotificationRequest request)
    {
        var workshopId = GetCurrentWorkshopId();

        // Parse template type (case-insensitive)
        if (!Enum.TryParse<EmailTemplateType>(request.TemplateType, ignoreCase: true, out var templateType))
        {
            return BadRequest(new { message = $"Invalid template type: {request.TemplateType}. Valid values are: CarReady, UpcomingVisit, PartsNeeded." });
        }

        // Validate client exists and belongs to workshop
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == request.ClientId && c.WorkshopId == workshopId);

        if (client == null)
        {
            return NotFound(new { message = "Client not found or does not belong to your workshop." });
        }

        // Resolve destination email
        var destinationEmail = !string.IsNullOrWhiteSpace(request.EmailOverride?.Trim())
            ? request.EmailOverride.Trim()
            : client.Email;

        if (string.IsNullOrWhiteSpace(destinationEmail))
        {
            return BadRequest(new { message = "No email address available. Please provide an email address." });
        }

        // Basic email validation
        if (!new EmailAddressAttribute().IsValid(destinationEmail))
        {
            return BadRequest(new { message = "Invalid email address format." });
        }

        // Validate service if provided
        Service? service = null;
        if (request.ServiceId.HasValue)
        {
            service = await _context.Services
                .Include(s => s.Vehicle)
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId.Value && s.Vehicle.Client.WorkshopId == workshopId);

            if (service == null)
            {
                return BadRequest(new { message = "Service not found or does not belong to your workshop." });
            }

            // Validate service matches client
            if (service.Vehicle.ClientId != request.ClientId)
            {
                return BadRequest(new { message = "Service does not belong to the specified client." });
            }
        }

        // Get workshop info
        var workshop = await _context.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return StatusCode(500, new { message = "Workshop configuration error." });
        }

        // Build template model
        var model = new EmailTemplateModel
        {
            ClientName = client.Name,
            WorkshopName = workshop.WorkshopName,
            WorkshopPhone = "66238950"
        };

        if (service != null)
        {
            model.VehiclePlate = service.Vehicle.PlateNumber;
            model.VehicleBrand = service.Vehicle.Brand;
            model.VehicleModel = service.Vehicle.Model;
            model.ServiceDate = service.ExitDate;
            model.ServiceCost = service.Cost;
            model.NextServiceDate = service.NextServiceDate;
            model.NextServiceMileage = service.NextServiceMileageTarget;
        }

        if (!string.IsNullOrWhiteSpace(request.PartsDescription))
        {
            model.PartsDescription = request.PartsDescription;
        }

        // Attempt to send email
        var logEntry = new EmailLog
        {
            WorkshopId = workshopId,
            ClientId = request.ClientId,
            CorreoDestino = destinationEmail,
            TipoEnvio = templateType.ToString(),
            FechaEnvio = DateTime.UtcNow,
            Estado = false
        };

        try
        {
            await _mailer.SendTemplateAsync(destinationEmail, templateType, model);
            logEntry.Estado = true;
        }
        catch (Exception ex)
        {
            logEntry.ErrorMessage = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
        }

        // Save log entry
        _context.EmailLogs.Add(logEntry);
        await _context.SaveChangesAsync();

        if (!logEntry.Estado)
        {
            return StatusCode(502, new { message = "Failed to send email. Please check your SMTP configuration and try again." });
        }

        return Ok(new { sent = true, logId = logEntry.IdLog });
    }

    /// <summary>Re-enviar "Auto listo" para un servicio completado.</summary>
    [HttpPost("services/{serviceId:guid}/completed-email")]
    public async Task<IActionResult> ResendCompletedEmail(Guid serviceId)
    {
        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null) return NotFound();
        if (service.ExitDate == null) return BadRequest(new { message = "El servicio no está completado." });

        var client = service.Vehicle.Client;
        if (string.IsNullOrWhiteSpace(client.Email))
            return BadRequest(new { message = "El cliente no tiene email." });

        // Get workshop info
        var workshop = await _context.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return StatusCode(500, new { message = "Workshop configuration error." });
        }

        // Build template model
        var model = new EmailTemplateModel
        {
            ClientName = client.Name,
            WorkshopName = workshop.WorkshopName,
            WorkshopPhone = "66238950",
            VehiclePlate = service.Vehicle.PlateNumber,
            VehicleBrand = service.Vehicle.Brand,
            VehicleModel = service.Vehicle.Model,
            ServiceDate = service.ExitDate,
            ServiceCost = service.Cost
        };

        // Create log entry
        var logEntry = new EmailLog
        {
            WorkshopId = workshopId,
            ClientId = client.Id,
            CorreoDestino = client.Email,
            TipoEnvio = EmailTemplateType.CarReady.ToString(),
            FechaEnvio = DateTime.UtcNow,
            Estado = false
        };

        try
        {
            // Send email
            await _mailer.SendTemplateAsync(client.Email, EmailTemplateType.CarReady, model);
            logEntry.Estado = true;
        }
        catch (Exception ex)
        {
            logEntry.ErrorMessage = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
        }

        // Save log entry
        _context.EmailLogs.Add(logEntry);
        await _context.SaveChangesAsync();

        if (!logEntry.Estado)
        {
            return StatusCode(502, new { message = "Error al enviar el correo. Verifica la configuración SMTP." });
        }

        return Ok(new { sent = true, logId = logEntry.IdLog });
    }

    /// <summary>Enviar manualmente recordatorio de Próximo Servicio para un servicio puntual.</summary>
    [HttpPost("services/{serviceId:guid}/upcoming-email")]
    public async Task<IActionResult> SendUpcomingForService(
        Guid serviceId,
        [FromQuery] bool enforceWindow = false,
        [FromQuery] int maxDaysAhead = 60)
    {
        var workshopId = GetCurrentWorkshopId();

        var s = await _context.Services
            .Include(x => x.Vehicle).ThenInclude(v => v.Client)
            .FirstOrDefaultAsync(x => x.Id == serviceId && x.Vehicle.Client.WorkshopId == workshopId);

        if (s == null) return NotFound();
        if (s.NextServiceDate == null)
            return BadRequest(new { message = "Este servicio no tiene Próximo Servicio configurado." });

        var to = s.Vehicle.Client.Email;
        if (string.IsNullOrWhiteSpace(to))
            return BadRequest(new { message = "El cliente no tiene email." });

        if (enforceWindow)
        {
            var daysLeft = DaysUntil(s.NextServiceDate);
            if (daysLeft is null || daysLeft > maxDaysAhead)
                return BadRequest(new { message = $"Fuera de ventana: faltan {daysLeft} días (> {maxDaysAhead})." });
        }

        await _mailer.SendUpcomingServiceReminderAsync(
            to: to,
            clientName: s.Vehicle.Client.Name,
            plate: s.Vehicle.PlateNumber,
            nextDate: s.NextServiceDate.Value,
            nextMileageTarget: s.NextServiceMileageTarget
        );

        return Ok(new { sent = true });
    }

    /// <summary>Previsualiza "Próximo Servicio" en un rango [from,to].</summary>
    [HttpGet("upcoming")]
    public async Task<ActionResult<IEnumerable<UpcomingPreviewDto>>> PreviewUpcoming(
        [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var workshopId = GetCurrentWorkshopId();
        var fromDate = from.Date;
        var toDate = to.Date;

        var items = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Where(s =>
                s.Vehicle.Client.WorkshopId == workshopId &&
                s.NextServiceDate != null &&
                s.NextServiceDate.Value.Date >= fromDate &&
                s.NextServiceDate.Value.Date <= toDate)
            .OrderBy(s => s.NextServiceDate)
            .Select(s => new UpcomingPreviewDto
            {
                ServiceId = s.Id,
                VehicleId = s.VehicleId,
                Plate = s.Vehicle.PlateNumber,
                Brand = s.Vehicle.Brand,
                Model = s.Vehicle.Model,
                Year = s.Vehicle.Year,
                ClientName = s.Vehicle.Client.Name,
                Email = s.Vehicle.Client.Email ?? "",
                NextServiceDate = s.NextServiceDate!.Value,
                NextServiceMileageTarget = s.NextServiceMileageTarget,
                DaysLeft = (s.NextServiceDate!.Value.Date - DateTime.UtcNow.Date).Days
            })
            .ToListAsync();

        return Ok(items);
    }

    /// <summary>Envío en lote (ids de Service) para "Próximo Servicio".</summary>
    [HttpPost("upcoming/send")]
    public async Task<ActionResult<BulkResultDto>> SendUpcomingBulk([FromBody] BulkSendRequest body)
    {
        var workshopId = GetCurrentWorkshopId();

        var services = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Where(s => body.Ids.Contains(s.Id) && s.Vehicle.Client.WorkshopId == workshopId)
            .ToListAsync();

        var result = new BulkResultDto();

        foreach (var s in services)
        {
            if (s.NextServiceDate == null || string.IsNullOrWhiteSpace(s.Vehicle.Client.Email))
            {
                result.Skipped++;
                continue;
            }

            try
            {
                await _mailer.SendUpcomingServiceReminderAsync(
                    to: s.Vehicle.Client.Email!,
                    clientName: s.Vehicle.Client.Name,
                    plate: s.Vehicle.PlateNumber,
                    nextDate: s.NextServiceDate.Value,
                    nextMileageTarget: s.NextServiceMileageTarget
                );
                result.Sent++;
            }
            catch
            {
                result.Errors.Add(s.Id);
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Escaneo automático: busca servicios con NextServiceDate en [hoy, hoy+days]
    /// y envía solo cuando faltan exactamente X días (onlyOn=7,3,1,0 por defecto).
    /// Si dryRun=true, no envía; solo devuelve candidatos.
    /// </summary>
    [HttpPost("upcoming/scan")]
    public async Task<IActionResult> ScanUpcoming(
        [FromQuery] int days = 7,
        [FromQuery] string? onlyOn = "7,3,1,0",
        [FromQuery] bool dryRun = false)
    {
        var workshopId = GetCurrentWorkshopId();
        var today = DateTime.UtcNow.Date;
        var until = today.AddDays(days);
        var allowed = ParseOnlyOn(onlyOn);

        var baseQuery = _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Where(s =>
                s.Vehicle.Client.WorkshopId == workshopId &&
                s.NextServiceDate != null &&
                s.NextServiceDate.Value.Date >= today &&
                s.NextServiceDate.Value.Date <= until &&
                !string.IsNullOrWhiteSpace(s.Vehicle.Client.Email));

        var list = await baseQuery.ToListAsync();

        var candidates = list
            .Select(s => new
            {
                Service = s,
                DaysLeft = DaysUntil(s.NextServiceDate) ?? int.MaxValue
            })
            .Where(x => allowed.Contains(x.DaysLeft))
            .OrderBy(x => x.Service.NextServiceDate)
            .ToList();

        if (dryRun)
        {
            var preview = candidates.Select(x => new UpcomingPreviewDto
            {
                ServiceId = x.Service.Id,
                VehicleId = x.Service.VehicleId,
                Plate = x.Service.Vehicle.PlateNumber,
                Brand = x.Service.Vehicle.Brand,
                Model = x.Service.Vehicle.Model,
                Year = x.Service.Vehicle.Year,
                ClientName = x.Service.Vehicle.Client.Name,
                Email = x.Service.Vehicle.Client.Email ?? "",
                NextServiceDate = x.Service.NextServiceDate!.Value,
                NextServiceMileageTarget = x.Service.NextServiceMileageTarget,
                DaysLeft = x.DaysLeft
            }).ToList();

            return Ok(preview);
        }

        var result = new BulkResultDto();

        foreach (var x in candidates)
        {
            try
            {
                await _mailer.SendUpcomingServiceReminderAsync(
                    to: x.Service.Vehicle.Client.Email!,
                    clientName: x.Service.Vehicle.Client.Name,
                    plate: x.Service.Vehicle.PlateNumber,
                    nextDate: x.Service.NextServiceDate!.Value,
                    nextMileageTarget: x.Service.NextServiceMileageTarget
                );
                result.Sent++;
            }
            catch
            {
                result.Errors.Add(x.Service.Id);
            }
        }

        return Ok(result);
    }

    /// <summary>Get email logs for the current workshop with advanced filtering.</summary>
    [HttpGet("logs")]
    public async Task<ActionResult<IEnumerable<EmailLogDto>>> GetEmailLogs(
        [FromQuery] Guid? clientId = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? status = null,
        [FromQuery] string? templateType = null,
        [FromQuery] int limit = 100)
    {
        var workshopId = GetCurrentWorkshopId();

        // Enforce max limit
        if (limit > 500) limit = 500;
        if (limit < 1) limit = 100;

        // Normalize dates to UTC to avoid Npgsql timestamptz issues
        // ASP.NET model binding creates DateTime with Kind=Unspecified from date-only query strings,
        // which Npgsql rejects for timestamptz columns. We need to ensure all DateTime values are UTC.
        DateTime normalizedDateFrom;
        DateTime normalizedDateTo;

        if (dateFrom.HasValue)
        {
            // Normalize dateFrom to UTC - treat as start of day (00:00:00) in UTC
            var tempFrom = dateFrom.Value;
            if (tempFrom.Kind == DateTimeKind.Unspecified)
            {
                // Treat unspecified as UTC
                normalizedDateFrom = DateTime.SpecifyKind(tempFrom.Date, DateTimeKind.Utc);
            }
            else if (tempFrom.Kind == DateTimeKind.Local)
            {
                // Convert local to UTC
                normalizedDateFrom = tempFrom.ToUniversalTime().Date;
            }
            else
            {
                // Already UTC
                normalizedDateFrom = tempFrom.Date;
            }
        }
        else
        {
            // Default to last 7 days
            normalizedDateFrom = DateTime.UtcNow.AddDays(-7).Date;
        }

        if (dateTo.HasValue)
        {
            // Normalize dateTo to UTC - treat as end of day (23:59:59.9999999) in UTC
            var tempTo = dateTo.Value;
            if (tempTo.Kind == DateTimeKind.Unspecified)
            {
                // Treat unspecified as UTC and set to end of day
                normalizedDateTo = DateTime.SpecifyKind(tempTo.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            }
            else if (tempTo.Kind == DateTimeKind.Local)
            {
                // Convert local to UTC and set to end of day
                normalizedDateTo = tempTo.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
            }
            else
            {
                // Already UTC, set to end of day
                normalizedDateTo = tempTo.Date.AddDays(1).AddTicks(-1);
            }
        }
        else
        {
            // Default to end of today
            normalizedDateTo = DateTime.UtcNow.Date.AddDays(1).AddTicks(-1);
        }

        var query = _context.EmailLogs
            .Where(l => l.WorkshopId == workshopId && l.FechaEnvio >= normalizedDateFrom && l.FechaEnvio <= normalizedDateTo);

        if (clientId.HasValue)
        {
            query = query.Where(l => l.ClientId == clientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(templateType) && templateType != "All")
        {
            query = query.Where(l => l.TipoEnvio == templateType);
        }

        // Status filter: Successful, Failed, Skipped
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            if (status == "Successful")
            {
                query = query.Where(l => l.Estado == true);
            }
            else if (status == "Failed")
            {
                query = query.Where(l => l.Estado == false && (l.ErrorMessage == null || !l.ErrorMessage.Contains("Omitido")));
            }
            else if (status == "Skipped")
            {
                query = query.Where(l => l.Estado == false && l.ErrorMessage != null && l.ErrorMessage.Contains("Omitido"));
            }
        }

        var logs = await query
            .OrderByDescending(l => l.FechaEnvio)
            .Take(limit)
            .ToListAsync();

        // Load client names in a separate query for performance
        var clientIds = logs.Select(l => l.ClientId).Distinct().ToList();
        var clients = await _context.Clients
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name);

        var result = logs.Select(l => new EmailLogDto
        {
            Id = l.IdLog,
            ClientId = l.ClientId,
            ClientName = clients.ContainsKey(l.ClientId) ? clients[l.ClientId] : null,
            Email = l.CorreoDestino,
            TemplateType = l.TipoEnvio,
            SentAt = l.FechaEnvio,
            Success = l.Estado,
            ErrorMessage = l.ErrorMessage,
            Status = l.Estado == true ? "Successful" :
                     (l.ErrorMessage != null && l.ErrorMessage.Contains("Omitido")) ? "Skipped" : "Failed"
        }).ToList();

        return Ok(result);
    }
}
