using AutoClient.Data;
using AutoClient.DTOs.Services;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("services")]
public class ServicesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ServicesController(ApplicationDbContext context)
    {
        _context = context;
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
            .Include(s => s.Vehicle)
                .ThenInclude(v => v.Client)
            .Include(s => s.Worker)
            .FirstOrDefaultAsync(s => s.Id == id && s.Vehicle.Client.WorkshopId == workshopId);

        if (service == null)
            return NotFound();

        service.NextServiceDate = dto.NextServiceDate;
        service.Cost = dto.Cost;
        service.MechanicNotes += $"\nFinal Notes: {dto.FinalObservations}";
        service.CreatedBy = dto.DeliveredBy ?? service.CreatedBy;
        service.Description += $"\nEstado Final: {dto.VehicleState}";
        service.ExitDate = dto.ExitDate ?? DateTime.UtcNow;

        await _context.SaveChangesAsync();
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
                PlateNumber = vehicle.PlateNumber,
                ClientName = vehicle.Client.Name,
                ExitDate = s.ExitDate,
                WorkerName = s.Worker.Name
            }).ToListAsync();

        return Ok(services);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ServiceResponseDto>> GetServiceById(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var service = await _context.Services
            .Include(s => s.Vehicle)
                .ThenInclude(v => v.Client)
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
            MechanicNotes = service.MechanicNotes,
            PlateNumber = service.Vehicle.PlateNumber,
            ClientName = service.Vehicle.Client.Name,
            ExitDate = service.ExitDate,
            WorkerName = service.Worker.Name
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceResponseDto>>> GetAllServices()
    {
        var workshopId = GetCurrentWorkshopId();

        var services = await _context.Services
            .Include(s => s.Vehicle)
                .ThenInclude(v => v.Client)
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
                Brand = s.Vehicle.Brand,
                Model = s.Vehicle.Model,
                Year = s.Vehicle.Year,
                ClientName = s.Vehicle.Client.Name,
                ExitDate = s.ExitDate,
                WorkerName = s.Worker.Name,

            })
            .ToListAsync();

        return Ok(services);
    }

    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
}