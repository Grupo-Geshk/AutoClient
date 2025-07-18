using AutoClient.Data;
using AutoClient.DTOs.Clients;
using AutoClient.DTOs.Vehicles;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[ApiController]
[Route("clients")]
[Authorize]
public class ClientsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
    public ClientsController(ApplicationDbContext context)
    {
        _context = context;
    }
   

    [HttpPost]
    public async Task<ActionResult<ClientSummaryDto>> CreateClient([FromBody] CreateClientDto dto)
    {
        var workshopIdClaim = User.FindFirst("workshop_id")?.Value;

        if (workshopIdClaim == null || !Guid.TryParse(workshopIdClaim, out var workshopId))
            return Unauthorized(new { message = "Invalid token." });

        var client = new Client
        {
            WorkshopId = workshopId,
            Name = dto.Name,
            Phone = dto.Phone,
            DNI = dto.DNI,
            Email = dto.Email,
            Address = dto.Address,
            CreatedAt = DateTime.UtcNow
        };

        _context.Clients.Add(client);
        await _context.SaveChangesAsync();

        return Ok(new ClientSummaryDto
        {
            Id = client.Id,
            Name = client.Name,
            Phone = client.Phone,
            DNI = client.DNI
        });
    }
    // Lista con búsqueda opcional
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientSummaryDto>>> GetClients([FromQuery] string? search)
    {
        var workshopId = GetCurrentWorkshopId();

        var query = _context.Clients
            .Where(c => c.WorkshopId == workshopId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c =>
                c.Name.Contains(search) ||
                c.DNI.Contains(search));
        }

        var results = await query
            .Select(c => new ClientSummaryDto
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                DNI = c.DNI
            })
            .ToListAsync();

        return Ok(results);
    }
    // GET /clients/{id}/vehicles
    [HttpGet("{id}/vehicles")]
    public async Task<ActionResult<IEnumerable<VehicleDto>>> GetVehiclesByClient(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkshopId == workshopId);

        if (client == null)
            return NotFound();

        var vehicles = await _context.Vehicles
            .Where(v => v.ClientId == id)
            .Select(v => new VehicleDto
            {
                Id = v.Id,
                PlateNumber = v.PlateNumber,
                Brand = v.Brand,
                Model = v.Model,
                Color = v.Color,
                Year = v.Year
            }).ToListAsync();

        return Ok(vehicles);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ClientDetailDto>> GetClientById(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .Where(c => c.Id == id && c.WorkshopId == workshopId)
            .FirstOrDefaultAsync();

        if (client == null)
            return NotFound();

        var dto = new ClientDetailDto
        {
            Id = client.Id,
            Name = client.Name,
            Phone = client.Phone,
            DNI = client.DNI,
            Email = client.Email,
            Address = client.Address
        };

        return Ok(dto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateClient(Guid id, [FromBody] UpdateClientDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkshopId == workshopId);

        if (client == null)
            return NotFound();

        client.Name = dto.Name;
        client.Phone = dto.Phone;
        client.DNI = dto.DNI;
        client.Email = dto.Email;
        client.Address = dto.Address;

        await _context.SaveChangesAsync();
        return NoContent();
    }
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteClient(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .Include(c => c.Vehicles)
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkshopId == workshopId);

        if (client == null)
            return NotFound();

        if (client.Vehicles.Any())
            return BadRequest(new { message = "Cannot delete client with associated vehicles." });

        _context.Clients.Remove(client);
        await _context.SaveChangesAsync();

        return NoContent();
    }

}
