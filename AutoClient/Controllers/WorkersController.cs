using AutoClient.Data;
using AutoClient.DTOs.Workers;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("workers")]
public class WorkersController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public WorkersController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<WorkerResponseDto>> CreateWorker([FromBody] CreateWorkerDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var worker = new Worker
        {
            WorkshopId = workshopId,
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            Cedula = dto.Cedula,
            Role = dto.Role
        };

        _context.Workers.Add(worker);
        await _context.SaveChangesAsync();

        return Ok(new WorkerResponseDto
        {
            Id = worker.Id,
            Name = worker.Name,
            Email = worker.Email,
            Phone = worker.Phone,
            Cedula = worker.Cedula,
            Role = worker.Role,
            CreatedAt = worker.CreatedAt
        });
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkerResponseDto>>> GetAllWorkers()
    {
        var workshopId = GetCurrentWorkshopId();

        var workers = await _context.Workers
            .Where(w => w.WorkshopId == workshopId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new WorkerResponseDto
            {
                Id = w.Id,
                Name = w.Name,
                Email = w.Email,
                Phone = w.Phone,
                Cedula = w.Cedula,
                Role = w.Role,
                CreatedAt = w.CreatedAt
            })
            .ToListAsync();

        return Ok(workers);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<WorkerResponseDto>> GetWorkerById(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var worker = await _context.Workers
            .FirstOrDefaultAsync(w => w.Id == id && w.WorkshopId == workshopId);

        if (worker == null)
            return NotFound();

        return Ok(new WorkerResponseDto
        {
            Id = worker.Id,
            Name = worker.Name,
            Email = worker.Email,
            Cedula= worker.Cedula,  
            Phone = worker.Phone,
            Role = worker.Role,
            CreatedAt = worker.CreatedAt
        });
    }

    // WorkersController.cs  (agrega debajo de GetWorkerById / antes de Delete)
    [HttpPut("{id}")]
    public async Task<ActionResult<WorkerResponseDto>> UpdateWorker(Guid id, [FromBody] UpdateWorkerDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var worker = await _context.Workers
            .FirstOrDefaultAsync(w => w.Id == id && w.WorkshopId == workshopId);

        if (worker == null) return NotFound();

        // Actualizaciones (si viene null, mantiene el valor actual)
        if (!string.IsNullOrWhiteSpace(dto.Name)) worker.Name = dto.Name.Trim();
        if (dto.Email != null) worker.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        if (dto.Phone != null) worker.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        if (dto.Role != null) worker.Role = string.IsNullOrWhiteSpace(dto.Role) ? null : dto.Role.Trim();
        if (dto.Cedula != null) worker.Cedula = string.IsNullOrWhiteSpace(dto.Cedula) ? null : dto.Cedula.Trim();

        await _context.SaveChangesAsync();

        return Ok(new WorkerResponseDto
        {
            Id = worker.Id,
            Name = worker.Name,
            Email = worker.Email,
            Phone = worker.Phone,
            Cedula = worker.Cedula,
            Role = worker.Role,
            CreatedAt = worker.CreatedAt
        });
    }


    // Controllers/WorkersController.cs  (añade debajo de GetWorkerById)
    [HttpGet("{id}/overview")]
    public async Task<ActionResult<WorkerOverviewDto>> GetWorkerOverview(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        // Datos del worker (scoped al taller actual)
        var worker = await _context.Workers
            .FirstOrDefaultAsync(w => w.Id == id && w.WorkshopId == workshopId);

        if (worker == null) return NotFound();

        // Conteo de servicios completados por ese worker en todo el histórico del taller
        var completedCount = await _context.Services
            .Include(s => s.Vehicle).ThenInclude(v => v.Client)
            .Where(s => s.Vehicle.Client.WorkshopId == workshopId
                        && s.WorkerId == id
                        && s.ExitDate != null)
            .CountAsync();

        return Ok(new WorkerOverviewDto
        {
            Id = worker.Id,
            Name = worker.Name,
            Email = worker.Email,
            Phone = worker.Phone,
            Cedula = worker.Cedula,
            Role = worker.Role,
            CreatedAt = worker.CreatedAt,
            CompletedServices = completedCount
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWorker(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var worker = await _context.Workers
            .FirstOrDefaultAsync(w => w.Id == id && w.WorkshopId == workshopId);

        if (worker == null)
            return NotFound();

        _context.Workers.Remove(worker);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
}
