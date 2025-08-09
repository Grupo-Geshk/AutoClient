using AutoClient.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public DashboardController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Devuelve el resumen de dashboard para un rango de fechas.
    /// </summary>
    /// <param name="from">Fecha de inicio del rango</param>
    /// <param name="to">Fecha de fin del rango</param>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to)
    {
        var workshopId = GetWorkshopId();

        // Servicios completados en el rango
        var completed = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Include(s => s.Worker) // <-- Esto es necesario
            .Where(s =>
                s.Vehicle.Client.WorkshopId == workshopId &&
                s.ExitDate != null &&
                s.ExitDate.Value >= from.UtcDateTime &&
                s.ExitDate.Value <= to.UtcDateTime)
            .ToListAsync();

        var totalRevenue = completed.Sum(s => s.Cost ?? 0);

        // Servicios pendientes (sin fecha de salida)
        var pending = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId && s.ExitDate == null)
            .ToListAsync();

        // Trabajador más eficiente (más servicios completados en rango)
        var bestWorker = completed
            .Where(s => s.WorkerId != null && s.Worker != null)
            .GroupBy(s => s.Worker!)
            .Select(g => new
            {
                Name = g.Key.Name,
                Count = g.Count()
            })
            .OrderByDescending(g => g.Count)
            .FirstOrDefault();


        // Servicios más realizados
        var topServices = completed
            .GroupBy(s => s.ServiceType)
            .Select(g => new { Service = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(5)
            .ToList();

        return Ok(new
        {
            Range = new
            {
                From = from.ToString("yyyy-MM-dd"),
                To = to.ToString("yyyy-MM-dd")
            },
            TotalCompleted = completed.Count,
            TotalRevenue = totalRevenue,
            PendingServices = pending.Select(s => new
            {
                s.Id,
                s.ServiceType,
                s.Description,
                s.Date,
                s.MileageAtService,
                ClientName = s.Vehicle.Client.Name,
                Plate = s.Vehicle.PlateNumber
            }),
            TopWorker = bestWorker,
            TopServices = topServices
        });
    }

    private Guid GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim!);
    }
}
