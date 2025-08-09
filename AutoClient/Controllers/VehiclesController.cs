using AutoClient.Data;
using AutoClient.DTOs.Vehicles;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Controllers;

[Authorize]
[ApiController]
[Route("vehicles")]
public class VehiclesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public VehiclesController(ApplicationDbContext context)
    {
        _context = context;
    }

    // POST /vehicles
    [HttpPost]
    public async Task<ActionResult<VehicleDetailDto>> CreateVehicle([FromBody] CreateVehicleDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == dto.ClientId && c.WorkshopId == workshopId);

        if (client == null)
            return NotFound(new { message = "Client not found or does not belong to this workshop." });

        var vehicle = new Vehicle
        {
            ClientId = dto.ClientId,
            PlateNumber = dto.PlateNumber,
            Brand = dto.Brand,
            Model = dto.Model,
            Year = dto.Year,
            Color = dto.Color,
            VIN = dto.VIN,
            MileageAtRegistration = dto.MileageAtRegistration,
            CreatedAt = DateTime.UtcNow,
            ImageUrl = dto.ImageUrl,
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        return Ok(new VehicleDetailDto
        {
            Id = vehicle.Id,
            PlateNumber = vehicle.PlateNumber,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Year = vehicle.Year,
            Color = vehicle.Color,
            VIN = vehicle.VIN,
            MileageAtRegistration = vehicle.MileageAtRegistration,
            ImageUrl = vehicle.ImageUrl,
            ClientName = client.Name,
            LastMileage = null
        });
    }

    // GET /vehicles/by-plate?plate=XXX123
    [HttpGet("by-plate")]
    public async Task<ActionResult<VehicleDetailDto>> GetByPlate([FromQuery] string plate)
    {
        var workshopId = GetCurrentWorkshopId();

        var vehicle = await _context.Vehicles
            .Include(v => v.Client)
            .Where(v => v.PlateNumber.ToLower() == plate.ToLower() && v.Client.WorkshopId == workshopId)
            .FirstOrDefaultAsync();

        if (vehicle == null)
            return NotFound();

        var lastMileage = await _context.Services
            .Where(s => s.VehicleId == vehicle.Id)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.MileageAtService)
            .FirstOrDefaultAsync();

        return Ok(new VehicleDetailDto
        {
            Id = vehicle.Id,
            PlateNumber = vehicle.PlateNumber,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Year = vehicle.Year,
            Color = vehicle.Color,
            VIN = vehicle.VIN,
            MileageAtRegistration = vehicle.MileageAtRegistration,
            ImageUrl = vehicle.ImageUrl,
            ClientName = vehicle.Client.Name,
            LastMileage = lastMileage
        });
    }

    // GET /vehicles
    [HttpGet("vehicles")]
    public async Task<ActionResult<IEnumerable<VehicleDto>>> GetVehicles([FromQuery] string? search)
    {
        var workshopId = GetCurrentWorkshopId();

        var query = _context.Vehicles
            .Where(v => v.Client.WorkshopId == workshopId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v =>
                v.PlateNumber.Contains(search) ||
                v.Model.Contains(search) ||
                v.Brand.Contains(search));
        }

        var results = await query
            .Select(v => new VehicleDto
            {
                Id = v.Id,
                PlateNumber = v.PlateNumber,
                Model = v.Model,
                Brand = v.Brand,
                Year = v.Year,
                Color = v.Color,
                ImageUrl= v.ImageUrl,
                ClientId = v.ClientId,

            })
            .ToListAsync();

        return Ok(results);
    }

    // GET /vehicles/by-client/{clientId}
    [HttpGet("by-client/{clientId}")]
    public async Task<ActionResult<IEnumerable<VehicleDetailDto>>> GetByClient(Guid clientId)
    {
        var workshopId = GetCurrentWorkshopId();

        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.Id == clientId && c.WorkshopId == workshopId);

        if (client == null)
            return NotFound();

        var vehicles = await _context.Vehicles
            .Where(v => v.ClientId == clientId)
            .Select(v => new VehicleDetailDto
            {
                Id = v.Id,
                PlateNumber = v.PlateNumber,
                Brand = v.Brand,
                Model = v.Model,
                Year = v.Year,
                Color = v.Color,
                VIN = v.VIN,
                MileageAtRegistration = v.MileageAtRegistration,
                ImageUrl = v.ImageUrl,
                ClientName = client.Name,
                LastMileage = _context.Services
                    .Where(s => s.VehicleId == v.Id)
                    .OrderByDescending(s => s.CreatedAt)
                    .Select(s => s.MileageAtService)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(vehicles);
    }

    // PUT /vehicles/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateVehicle(Guid id, [FromBody] UpdateVehicleDto dto)
    {
        var workshopId = GetCurrentWorkshopId();

        var vehicle = await _context.Vehicles
            .Include(v => v.Client)
            .FirstOrDefaultAsync(v => v.Id == id && v.Client.WorkshopId == workshopId);

        if (vehicle == null)
            return NotFound();

        vehicle.Brand = dto.Brand;
        vehicle.Model = dto.Model;
        vehicle.Year = dto.Year;
        vehicle.Color = dto.Color;
        vehicle.VIN = dto.VIN;
        vehicle.MileageAtRegistration = dto.MileageAtRegistration;
        vehicle.ImageUrl = dto.ImageUrl;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE /vehicles/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteVehicle(Guid id)
    {
        var workshopId = GetCurrentWorkshopId();

        var vehicle = await _context.Vehicles
            .Include(v => v.Client)
            .FirstOrDefaultAsync(v => v.Id == id && v.Client.WorkshopId == workshopId);

        if (vehicle == null)
            return NotFound();

        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetCurrentWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.Parse(claim);
    }
}
