using AutoClient.Data;
using AutoClient.DTOs.Auth;
using AutoClient.Models;
using AutoClient.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;

namespace AutoClient.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITokenService _tokenService;

    public AuthController(ApplicationDbContext context, ITokenService tokenService)
    {
        _context = context;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] WorkshopLoginDto dto)
    {
        var workshop = await _context.Workshops
            .FirstOrDefaultAsync(w => w.Username == dto.Username);

        if (workshop == null || !VerifyPassword(dto.Password, workshop.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var token = _tokenService.CreateToken(workshop);

        return Ok(new AuthResponseDto
        {
            Token = token,
            WorkshopName = workshop.WorkshopName,
            Subdomain = workshop.Subdomain
        });
    }
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterWorkshopDto dto)
    {
        if (await _context.Workshops.AnyAsync(w => w.Username == dto.Username || w.Subdomain == dto.Subdomain))
        {
            return Conflict(new { message = "Username or subdomain already exists." });
        }

        var workshop = new Workshop
        {
            WorkshopName = dto.WorkshopName,
            Username = dto.Username,
            Email = dto.Email,
            Phone = dto.Phone,
            Subdomain = dto.Subdomain,
            PasswordHash = HashPassword(dto.Password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Workshops.Add(workshop);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Workshop registered successfully." });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<WorkshopMeDto>> Me()
    {
        var workshopIdClaim = User.FindFirst("workshop_id")?.Value;

        if (workshopIdClaim == null || !Guid.TryParse(workshopIdClaim, out var workshopId))
            return Unauthorized(new { message = "Invalid token." });

        var workshop = await _context.Workshops.FindAsync(workshopId);

        if (workshop == null)
            return NotFound(new { message = "Workshop not found." });

        var dto = new WorkshopMeDto
        {
            Id = workshop.Id,
            WorkshopName = workshop.WorkshopName,
            Username = workshop.Username,
            Email = workshop.Email,
            Phone = workshop.Phone,
            Subdomain = workshop.Subdomain
        };

        return Ok(dto);
    }

    string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private bool VerifyPassword(string inputPassword, string storedHash)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(inputPassword));
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
        return hash == storedHash;
    }
}
