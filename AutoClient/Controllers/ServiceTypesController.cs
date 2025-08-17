using AutoClient.Data;
using AutoClient.DTOs.ServiceTypes;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("service-types")]
public class ServiceTypesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public ServiceTypesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // GET /service-types
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceTypeResponseDto>>> GetAll()
    {
        var workshopId = GetCurrentWorkshopId();

        var list = await _context.ServiceTypes
            .Where(st => st.WorkshopId == workshopId)
            .OrderBy(st => st.ServiceTypeName)
            .Select(st => new ServiceTypeResponseDto
            {
                Id = st.Id,
                ServiceTypeName = st.ServiceTypeName,
                CreatedAt = st.CreatedAt
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST /service-types
    [HttpPost]
    public async Task<ActionResult<ServiceTypeResponseDto>> Create([FromBody] CreateServiceTypeDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        if (string.IsNullOrWhiteSpace(dto.ServiceTypeName))
            return BadRequest("ServiceTypeName es requerido.");

        // Unicidad por taller (opcional pero recomendable)
        var exists = await _context.ServiceTypes
            .AnyAsync(st => st.WorkshopId == workshopId && st.ServiceTypeName == dto.ServiceTypeName.Trim());

        if (exists)
            return Conflict("Ya existe un tipo de servicio con ese nombre.");

        var entity = new ServiceType
        {
            WorkshopId = workshopId,
            ServiceTypeName = dto.ServiceTypeName.Trim()
        };

        _context.ServiceTypes.Add(entity);
        await _context.SaveChangesAsync();

        var result = new ServiceTypeResponseDto
        {
            Id = entity.Id,
            ServiceTypeName = entity.ServiceTypeName,
            CreatedAt = entity.CreatedAt
        };

        return Ok(result);
    }

    // DELETE /service-types/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var entity = await _context.ServiceTypes
            .FirstOrDefaultAsync(st => st.Id == id && st.WorkshopId == workshopId);

        if (entity == null)
            return NotFound();

        _context.ServiceTypes.Remove(entity);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetCurrentWorkshopId()
    {
        // mismo patrón que WorkersController
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
}
