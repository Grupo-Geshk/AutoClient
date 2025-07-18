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

    // GET /dashboard/today-summary
    [HttpGet("today-summary")]
    public async Task<IActionResult> GetTodaySummary()
    {
        var workshopId = GetWorkshopId();
        var today = DateTime.UtcNow.Date;

        var servicesToday = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId &&
                        s.ExitDate.HasValue &&
                        s.ExitDate.Value.Date == today)
            .ToListAsync();

        var totalCost = servicesToday.Sum(s => s.Cost ?? 0);

        var pendingServices = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId && s.ExitDate == null)
            .ToListAsync();

        return Ok(new
        {
            TotalClosedToday = servicesToday.Count,
            TotalRevenueToday = totalCost,
            PendingServices = pendingServices.Select(s => new
            {
                s.Id,
                s.ServiceType,
                s.Description,
                s.Date,
                s.MileageAtService,
                ClientName = s.Vehicle.Client.Name,
                Plate = s.Vehicle.PlateNumber
            })
        });
    }
    [HttpGet("top-clients")]
    public async Task<IActionResult> GetTopClients([FromQuery] int month, [FromQuery] int year)
    {
        var workshopId = GetWorkshopId();

        var topClients = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId &&
                        s.Date.Month == month && s.Date.Year == year)
            .GroupBy(s => new { s.Vehicle.Client.Id, s.Vehicle.Client.Name })
            .Select(g => new { g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync();

        return Ok(topClients);
    }
    [HttpGet("top-services")]
    public async Task<IActionResult> GetTopServices([FromQuery] int month, [FromQuery] int year)
    {
        var workshopId = GetWorkshopId();

        var serviceTypes = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId &&
                        s.Date.Month == month && s.Date.Year == year)
            .GroupBy(s => s.ServiceType)
            .Select(g => new { Service = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        return Ok(serviceTypes);
    }
    [HttpGet("services-per-day")]
    public async Task<IActionResult> GetServicesPerDay([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var workshopId = GetWorkshopId();

        var result = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId &&
                        s.Date >= from && s.Date <= to)
            .GroupBy(s => s.Date.Date)
            .Select(g => new { Date = g.Key.ToString("yyyy-MM-dd"), Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return Ok(result);
    }
    [HttpGet("average-delivery-time")]
    public async Task<IActionResult> GetAverageDeliveryTime()
    {
        var workshopId = GetWorkshopId();

        var services = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId && s.ExitDate.HasValue)
            .ToListAsync();

        if (!services.Any()) return Ok(new { AverageHours = 0 });

        var avgHours = services.Average(s => (s.ExitDate!.Value - s.Date).TotalHours);

        return Ok(new { AverageHours = Math.Round(avgHours, 2) });
    }
    [HttpGet("monthly-income")]
    public async Task<IActionResult> GetMonthlyIncome([FromQuery] int month, [FromQuery] int year)
    {
        var workshopId = GetWorkshopId();

        var income = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId &&
                        s.ExitDate.HasValue &&
                        s.ExitDate.Value.Month == month && s.ExitDate.Value.Year == year)
            .SumAsync(s => s.Cost ?? 0);

        return Ok(new { Month = month, Year = year, TotalIncome = income });
    }

    // GET /dashboard/pending-services
    [HttpGet("pending-services")]
    public async Task<IActionResult> GetPendingServices()
    {
        var workshopId = GetWorkshopId();

        var pending = await _context.Services
            .Include(s => s.Vehicle)
            .ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId && s.ExitDate == null)
            .OrderBy(s => s.Date)
            .Select(s => new
            {
                ServiceId = s.Id,
                Plate = s.Vehicle.PlateNumber,
                ClientName = s.Vehicle.Client.Name,
                EntryDate = s.Date,
                ServiceType = s.ServiceType,
                Mileage = s.MileageAtService
            })
            .ToListAsync();

        return Ok(pending);
    }


    private Guid GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim!);
    }

}
