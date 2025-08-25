using AutoClient.Data;
using AutoClient.DTOs.Auth;
using AutoClient.Models;
using AutoClient.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKit.Net.Smtp;
using AutoClient.Services;

namespace AutoClient.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly SmtpSettings _smtp;

    public AuthController(ApplicationDbContext context, ITokenService tokenService, IOptions<SmtpSettings> smtp)
    {
        _context = context;
        _tokenService = tokenService;
        _smtp = smtp.Value;
    }

    // Login directo SIN OTP/Email (uso temporal / admin)
    [HttpPost("adminLogin")]
    [AllowAnonymous]
    public async Task<IActionResult> AdminLogin([FromBody] WorkshopLoginDto dto)
    {
        // 1) Buscar taller
        var workshop = await _context.Workshops
            .FirstOrDefaultAsync(w => w.Username == dto.Username);

        // 2) Verificar credenciales
        if (workshop == null || !VerifyPassword(dto.Password, workshop.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials." });

        // 3) Emitir JWT (sin OTP, sin correo)
        var token = _tokenService.CreateToken(workshop);

        // (Opcional) si quieres seguir usando el cookie del dispositivo, lo puedes dejar:
        // var deviceToken = Guid.NewGuid().ToString();
        // Response.Cookies.Append("device_token", deviceToken, new CookieOptions
        // {
        //     HttpOnly = true,
        //     Secure = true,
        //     SameSite = SameSiteMode.Strict,
        //     Expires = DateTime.UtcNow.AddYears(1)
        // });

        return Ok(new AuthResponseDto
        {
            Token = token,
            WorkshopName = workshop.WorkshopName,
            Subdomain = workshop.Subdomain
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] WorkshopLoginDto dto)
    {
        var workshop = await _context.Workshops.FirstOrDefaultAsync(w => w.Username == dto.Username);
        if (workshop == null || !VerifyPassword(dto.Password, workshop.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials." });

        var deviceToken = Request.Cookies["device_token"];
        if (!string.IsNullOrEmpty(deviceToken) && await IsTrustedDeviceAsync(workshop.Id, deviceToken))
        {
            var token = _tokenService.CreateToken(workshop);
            return Ok(new AuthResponseDto
            {
                Token = token,
                WorkshopName = workshop.WorkshopName,
                Subdomain = workshop.Subdomain
            });
        }

        // Generar OTP
        var otpCode = new Random().Next(100000, 999999).ToString();
        var otpToken = Guid.NewGuid().ToString();
        var otpHash = HashString(otpCode);

        var loginOtp = new LoginOtp
        {
            WorkshopId = workshop.Id,
            CodeHash = otpHash,
            OtpToken = otpToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            MaxAttempts = 5
        };
        _context.LoginOtps.Add(loginOtp);
        await _context.SaveChangesAsync();

        // Enviar correo
        await SendOtpEmailAsync(workshop.Email, otpCode, workshop.WorkshopName);

        return Ok(new { needOtp = true, otpToken });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        var otpEntry = await _context.LoginOtps
            .Include(o => o.Workshop)
            .FirstOrDefaultAsync(o => o.OtpToken == dto.OtpToken);

        if (otpEntry == null || otpEntry.ExpiresAt < DateTime.UtcNow || otpEntry.Attempts >= otpEntry.MaxAttempts)
            return BadRequest(new { message = "Invalid or expired OTP." });

        otpEntry.Attempts++;

        if (otpEntry.CodeHash != HashString(dto.Code))
        {
            await _context.SaveChangesAsync();
            return BadRequest(new { message = "Incorrect OTP." });
        }

        // OTP válido: registrar dispositivo
        var deviceToken = Guid.NewGuid().ToString();
        var deviceTokenHash = HashString(deviceToken);

        var trustedDevice = new TrustedDevice
        {
            WorkshopId = otpEntry.WorkshopId,
            DeviceTokenHash = deviceTokenHash,
            UserAgent = Request.Headers.UserAgent.ToString(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
        };
        _context.TrustedDevices.Add(trustedDevice);
        _context.LoginOtps.Remove(otpEntry); // no se reutiliza

        await _context.SaveChangesAsync();

        // Enviar cookie
        Response.Cookies.Append("device_token", deviceToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddYears(1)
        });

        // Emitir JWT
        var token = _tokenService.CreateToken(otpEntry.Workshop);
        return Ok(new AuthResponseDto
        {
            Token = token,
            WorkshopName = otpEntry.Workshop.WorkshopName,
            Subdomain = otpEntry.Workshop.Subdomain
        });
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

        return Ok(new WorkshopMeDto
        {
            Id = workshop.Id,
            WorkshopName = workshop.WorkshopName,
            Username = workshop.Username,
            Email = workshop.Email,
            Phone = workshop.Phone,
            Subdomain = workshop.Subdomain
        });
    }

    private async Task<bool> IsTrustedDeviceAsync(Guid workshopId, string deviceToken)
    {
        var hash = HashString(deviceToken);
        return await _context.TrustedDevices
            .AnyAsync(td => td.WorkshopId == workshopId && td.DeviceTokenHash == hash && !td.IsRevoked);
    }

    private async Task SendOtpEmailAsync(string toEmail, string code, string workshopName)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.SenderName, _smtp.SenderEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = $"Código de verificación - {workshopName}";
        message.Body = new TextPart("plain")
        {
            Text = $"Tu código de verificación es: {code}\n\nEste código expira en 10 minutos."
        };

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, MailKit.Security.SecureSocketOptions.StartTls);
        await client.AuthenticateAsync(_smtp.Username, _smtp.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    private bool VerifyPassword(string inputPassword, string storedHash)
    {
        return HashPassword(inputPassword) == storedHash;
    }

    private string HashString(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }
}

// DTO para verificar OTP
public class VerifyOtpDto
{
    public string OtpToken { get; set; }
    public string Code { get; set; }
}
