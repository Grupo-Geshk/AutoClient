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
            Address = client.Address
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
            var s = search.Trim();
            var sDigits = new string(s.Where(char.IsDigit).ToArray());
            query = query.Where(c =>
            c.Name.Contains(s) ||
            c.Email.Contains(s) ||
            (!string.IsNullOrEmpty(sDigits) &&
            new string((c.Phone ?? "").Where(char.IsDigit).ToArray()).Contains(sDigits))
                    );
        }

        var results = await query
            .Select(c => new ClientSummaryDto
            {
                Id = c.Id,
                Name = c.Name,
                Phone = c.Phone,
                Email= c.Email,
                
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
            .Where(v => v.ClientId == id && v.Client.WorkshopId == workshopId)
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

        // Merge condicional (solo actualiza lo que venga)
        if (dto.Name is not null) client.Name = dto.Name;
        if (dto.Phone is not null) client.Phone = dto.Phone;

        if (dto.Email is not null)
        {
            // Si te envían string vacío, mantenemos el correo actual para no violar [Required]
            var trimmed = dto.Email.Trim();
            if (trimmed.Length == 0)
            {
                // No sobrescribir con vacío: mantenlo
            }
            else
            {
                // Validación light de formato. (Opcional: usar MailAddress)
                try
                {
                    var _ = new System.Net.Mail.MailAddress(trimmed);
                    client.Email = trimmed;
                }
                catch
                {
                    return BadRequest(new { message = "El correo tiene un formato inválido." });
                }
            }
        }

        if (dto.Address is not null) client.Address = dto.Address;

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
