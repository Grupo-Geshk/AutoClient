using AutoClient.Data;
using AutoClient.DTOs.Services;
using AutoClient.Models;
using AutoClient.Services.Email; // ⬅️ Mailer
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static AutoClient.Services.Email.IEmailTemplateRenderer; // EmailTemplateType

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("services")]
public class ServicesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IClientMailer _mailer; // ⬅️ mailer inyectado

    public ServicesController(ApplicationDbContext context, IClientMailer mailer)
    {
        _context = context;
        _mailer = mailer;
    }

    [HttpPost]
    public async Task<ActionResult<ServiceResponseDto>> CreateService([FromBody] CreateServiceDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var vehicle = await _context.Vehicles
            .Include(v => v.Client)
            .FirstOrDefaultAsync(v => v.Id == dto.VehicleId && v.Client.WorkshopId == workshopId);

        if (vehicle == null)
            return NotFound(new { message = "Vehicle not found or does not belong to this workshop." });

        var worker = await _context.Workers
            .FirstOrDefaultAsync(w => w.Id == dto.WorkerId && w.WorkshopId == workshopId);

        if (worker == null)
            return NotFound(new { message = "Worker not found or does not belong to this workshop." });

        var service = new Service
        {
            VehicleId = dto.VehicleId,
            WorkerId = dto.WorkerId,
            Date = dto.EntryDate ?? DateTime.UtcNow,
            MileageAtService = dto.Mileage,
            ServiceType = dto.ServiceType,
            Description = dto.Description,
            MechanicNotes = dto.MechanicNotes,
            CreatedBy = User.Identity?.Name ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _context.Services.Add(service);
        await _context.SaveChangesAsync();

        return Ok(new ServiceResponseDto
        {
            Id = service.Id,
            VehicleId = service.VehicleId,
            EntryDate = service.Date,
            Mileage = service.MileageAtService,
            ServiceType = service.ServiceType,
            Description = service.Description,
            MechanicNotes = service.MechanicNotes,
            PlateNumber = vehicle.PlateNumber,
            ClientName = vehicle.Client.Name,
            ExitDate = service.ExitDate,
            WorkerName = worker.Name
        });
    }

    [HttpPut("{id}/complete")]
    public async Task<IActionResult> CompleteService(Guid id, [FromBody] CompleteServiceDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .FirstOrDefaultAsync(s => s.Id == id && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null)
            return NotFound();

        service.NextServiceDate = dto.NextServiceDate;
        service.NextServiceMileageTarget = string.IsNullOrWhiteSpace(dto.NextServiceMileageTarget)
            ? "-" : dto.NextServiceMileageTarget.Trim();
        service.Cost = dto.Cost;

        if (!string.IsNullOrWhiteSpace(dto.FinalObservations))
            service.MechanicNotes = string.IsNullOrWhiteSpace(service.MechanicNotes)
                ? $"Final Notes: {dto.FinalObservations}"
                : $"{service.MechanicNotes}\nFinal Notes: {dto.FinalObservations}";

        service.CreatedBy = dto.DeliveredBy ?? service.CreatedBy;

        if (!string.IsNullOrWhiteSpace(dto.VehicleState))
            service.Description = string.IsNullOrWhiteSpace(service.Description)
                ? $"Estado Final: {dto.VehicleState}"
                : $"{service.Description}\nEstado Final: {dto.VehicleState}";

        service.ExitDate = dto.ExitDate ?? DateTime.UtcNow;

        await _context.SaveChangesAsync();

        // ========= AUTOMATIC NOTIFICATION: "Auto listo" =========
        // Trigger automatic email if automation is enabled for this workshop
        _ = Task.Run(async () =>
        {
            try
            {
                await TriggerVehicleDeliveredNotification(service.Id, workshopId);
            }
            catch (Exception ex)
            {
                // Log error but don't block the service completion
                Console.WriteLine($"Failed to send vehicle delivered notification: {ex.Message}");
            }
        });
        // ========================================================

        return NoContent();
    }

    [HttpGet("by-vehicle/{vehicleId}")]
    public async Task<ActionResult<IEnumerable<ServiceResponseDto>>> GetHistoryByVehicle(Guid vehicleId)
    {
        var workshopId = GetCurrentWorkshopId();

        var vehicle = await _context.Vehicles
            .Include(v => v.Client)
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.Client.WorkshopId == workshopId);

        if (vehicle == null)
            return NotFound();

        var services = await _context.Services
            .Include(s => s.Worker)
            .Where(s => s.VehicleId == vehicleId)
            .OrderByDescending(s => s.Date)
            .Select(s => new ServiceResponseDto
            {
                Id = s.Id,
                VehicleId = s.VehicleId,
                EntryDate = s.Date,
                Mileage = s.MileageAtService,
                ServiceType = s.ServiceType,
                Description = s.Description,
                MechanicNotes = s.MechanicNotes,
                NextServiceMileageTarget = s.NextServiceMileageTarget,
                PlateNumber = vehicle.PlateNumber,
                ClientName = vehicle.Client.Name,
                ExitDate = s.ExitDate,
                WorkerName = s.Worker.Name,
                Cost = s.Cost,
            }).ToListAsync();

        return Ok(services);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServiceResponseDto>> GetServiceById(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .FirstOrDefaultAsync(s => s.Id == id && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null)
            return NotFound();

        return Ok(new ServiceResponseDto
        {
            Id = service.Id,
            VehicleId = service.VehicleId,
            EntryDate = service.Date,
            Mileage = service.MileageAtService,
            ServiceType = service.ServiceType,
            Description = service.Description,
            NextServiceMileageTarget = service.NextServiceMileageTarget,
            MechanicNotes = service.MechanicNotes,
            PlateNumber = service.Vehicle.PlateNumber,
            ClientName = service.Vehicle.Client.Name,
            ExitDate = service.ExitDate,
            WorkerName = service.Worker.Name,
            Cost = service.Cost,
        });
    }

    [HttpPut("{id}/admin-update")]
    public async Task<IActionResult> AdminUpdate(Guid id, [FromBody] UpdateServiceAdminDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .FirstOrDefaultAsync(s => s.Id == id && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null) return NotFound();

        if (dto.EntryDate.HasValue) service.Date = dto.EntryDate.Value;
        if (dto.ExitDate.HasValue) service.ExitDate = dto.ExitDate.Value;
        if (dto.Mileage.HasValue) service.MileageAtService = dto.Mileage.Value;
        if (!string.IsNullOrWhiteSpace(dto.ServiceType)) service.ServiceType = dto.ServiceType.Trim();
        if (dto.Description != null) service.Description = dto.Description;
        if (dto.MechanicNotes != null) service.MechanicNotes = dto.MechanicNotes;
        if (dto.NextServiceDate.HasValue) service.NextServiceDate = dto.NextServiceDate.Value;
        if (dto.NextServiceMileageTarget != null)
            service.NextServiceMileageTarget = string.IsNullOrWhiteSpace(dto.NextServiceMileageTarget) ? "-" : dto.NextServiceMileageTarget.Trim();
        if (dto.Cost.HasValue) service.Cost = dto.Cost.Value;
        if (dto.WorkerId.HasValue) service.WorkerId = dto.WorkerId.Value;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceResponseDto>>> GetAllServices()
    {
        var workshopId = GetCurrentWorkshopId();

        var services = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId)
            .OrderByDescending(s => s.Date)
            .Select(s => new ServiceResponseDto
            {
                Id = s.Id,
                VehicleId = s.VehicleId,
                EntryDate = s.Date,
                Mileage = s.MileageAtService,
                ServiceType = s.ServiceType,
                Description = s.Description,
                MechanicNotes = s.MechanicNotes,
                PlateNumber = s.Vehicle.PlateNumber,
                NextServiceMileageTarget = s.NextServiceMileageTarget,
                Brand = s.Vehicle.Brand,
                Model = s.Vehicle.Model,
                Year = s.Vehicle.Year,
                ClientName = s.Vehicle.Client.Name,
                ExitDate = s.ExitDate,
                WorkerName = s.Worker.Name,
                Cost = s.Cost
            })
            .ToListAsync();

        return Ok(services);
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateServiceNotesDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Notes))
            return BadRequest(new { message = "Notes es requerido." });

        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .FirstOrDefaultAsync(s => s.Id == id && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null)
            return NotFound();

        var newNotes = dto.Notes.Trim();

        if (dto.Append == true)
        {
            service.MechanicNotes = string.IsNullOrWhiteSpace(service.MechanicNotes)
                ? newNotes
                : $"{service.MechanicNotes}\n{newNotes}";
        }
        else
        {
            service.MechanicNotes = newNotes;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            id = service.Id,
            mechanicNotes = service.MechanicNotes
        });
    }

    private async Task TriggerVehicleDeliveredNotification(Guid serviceId, Guid workshopId)
    {
        // Load automation settings
        var settings = await _context.WorkshopNotificationSettings
            .FirstOrDefaultAsync(s => s.WorkshopId == workshopId);

        // If automation is disabled or no settings exist, skip
        if (settings == null || !settings.VehicleDeliveredEnabled)
        {
            return;
        }

        // Load service with client info
        var service = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null)
        {
            return;
        }

        var client = service.Vehicle.Client;
        if (client == null)
        {
            return;
        }

        // Parse template type
        if (!Enum.TryParse<EmailTemplateType>(settings.VehicleDeliveredTemplate, ignoreCase: true, out var templateType))
        {
            templateType = EmailTemplateType.CarReady;
        }

        // Check if client has email (respect safety setting)
        if (settings.OnlyIfEmailExists && string.IsNullOrWhiteSpace(client.Email))
        {
            // Log as "Skipped: missing email"
            var skipLog = new EmailLog
            {
                WorkshopId = workshopId,
                ClientId = client.Id,
                CorreoDestino = "",
                TipoEnvio = templateType.ToString(),
                FechaEnvio = DateTime.UtcNow,
                Estado = false,
                ErrorMessage = "Omitido: falta correo"
            };
            _context.EmailLogs.Add(skipLog);
            await _context.SaveChangesAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(client.Email))
        {
            return; // No email and safety is off, just skip silently
        }

        // Get workshop info
        var workshop = await _context.Workshops.FindAsync(workshopId);
        if (workshop == null)
        {
            return;
        }

        // Build template model
        var model = new EmailTemplateModel
        {
            ClientName = client.Name,
            WorkshopName = workshop.WorkshopName,
            WorkshopPhone = workshop.Phone ?? "N/A",
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
            TipoEnvio = templateType.ToString(),
            FechaEnvio = DateTime.UtcNow,
            Estado = false
        };

        try
        {
            // Send email
            await _mailer.SendTemplateAsync(client.Email, templateType, model);
            logEntry.Estado = true;
        }
        catch (Exception ex)
        {
            logEntry.ErrorMessage = ex.Message.Length > 1000 ? ex.Message.Substring(0, 1000) : ex.Message;
        }

        // Save log entry
        _context.EmailLogs.Add(logEntry);
        await _context.SaveChangesAsync();
    }

    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
}
