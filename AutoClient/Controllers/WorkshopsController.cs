using AutoClient.Data;
using AutoClient.DTOs.Workshops;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoClient.Controllers;

[ApiController]
[Route("workshops")]
[Authorize]
public class WorkshopsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public WorkshopsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private Guid? GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    [HttpGet("me")]
    public async Task<ActionResult<WorkshopProfileDto>> GetProfile()
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var workshop = await _context.Workshops.FindAsync(workshopId.Value);
        if (workshop == null) return NotFound(new { message = "Workshop not found." });

        return Ok(ToDto(workshop));
    }

    [HttpPut("me")]
    public async Task<ActionResult<WorkshopProfileDto>> UpdateProfile([FromBody] UpdateWorkshopProfileDto dto)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var workshop = await _context.Workshops.FindAsync(workshopId.Value);
        if (workshop == null) return NotFound(new { message = "Workshop not found." });

        // Actualización parcial: solo campos presentes en el payload
        if (dto.WorkshopName != null) workshop.WorkshopName = dto.WorkshopName.Trim();
        if (dto.Email != null) workshop.Email = dto.Email.Trim();
        if (dto.Phone != null) workshop.Phone = dto.Phone.Trim();
        if (dto.Ruc != null) workshop.Ruc = dto.Ruc.Trim();
        if (dto.Dv != null) workshop.Dv = dto.Dv.Trim();
        if (dto.Address != null) workshop.Address = dto.Address.Trim();
        if (dto.BusinessDescription != null) workshop.BusinessDescription = dto.BusinessDescription.Trim();
        if (dto.Logo != null) workshop.Logo = dto.Logo.Trim();
        if (dto.NotificationEmail != null) workshop.NotificationEmail = dto.NotificationEmail.Trim();

        await _context.SaveChangesAsync();
        return Ok(ToDto(workshop));
    }

    private static WorkshopProfileDto ToDto(Workshop w) => new()
    {
        Id = w.Id,
        WorkshopName = w.WorkshopName,
        Username = w.Username,
        Email = w.Email,
        Phone = w.Phone,
        Subdomain = w.Subdomain,
        Ruc = w.Ruc,
        Dv = w.Dv,
        Address = w.Address,
        BusinessDescription = w.BusinessDescription,
        Logo = w.Logo,
        NotificationEmail = w.NotificationEmail,
        CreatedAt = w.CreatedAt
    };
}
