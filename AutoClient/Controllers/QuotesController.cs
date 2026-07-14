using AutoClient.Data;
using AutoClient.DTOs.Quotes;
using AutoClient.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace AutoClient.Controllers;

[ApiController]
[Route("quotes")]
[Authorize]
public class QuotesController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public QuotesController(ApplicationDbContext context)
    {
        _context = context;
    }

    private Guid? GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    // GET /quotes?status=pending
    [HttpGet]
    public async Task<ActionResult<List<QuoteSummaryDto>>> List([FromQuery] string? status)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var query = _context.Quotes
            .Where(q => q.WorkshopId == workshopId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(q => q.Status == status);

        var quotes = await query
            .OrderByDescending(q => q.QuoteNumber)
            .Select(q => new QuoteSummaryDto
            {
                Id = q.Id,
                QuoteNumber = q.QuoteNumber,
                QuoteDate = q.QuoteDate,
                ValidUntil = q.ValidUntil,
                ClientName = q.ClientName,
                VehicleInfo = q.VehicleInfo,
                Status = q.Status,
                ShareToken = q.ShareToken,
                Total = q.Total,
                ItemCount = q.Items.Count,
                CreatedAt = q.CreatedAt
            })
            .ToListAsync();

        return Ok(quotes);
    }

    // GET /quotes/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> Get(Guid id)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var quote = await _context.Quotes
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == id && q.WorkshopId == workshopId.Value);

        if (quote == null) return NotFound(new { message = "Cotización no encontrada." });

        return Ok(ToDto(quote));
    }

    // POST /quotes
    [HttpPost]
    public async Task<ActionResult<QuoteDto>> Create([FromBody] QuoteCreateDto dto)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(dto.ClientName))
            return BadRequest(new { message = "El nombre del cliente es requerido." });
        if (dto.Items.Count == 0)
            return BadRequest(new { message = "La cotización debe tener al menos un ítem." });
        if (dto.Items.Any(i => string.IsNullOrWhiteSpace(i.Description) || i.Quantity <= 0 || i.UnitPrice < 0))
            return BadRequest(new { message = "Todos los ítems requieren descripción, cantidad positiva y precio válido." });
        if (dto.TaxRate is < 0 or > 100)
            return BadRequest(new { message = "Tasa de impuesto inválida." });

        // Número elegido por el usuario, o correlativo automático si viene vacío
        long quoteNumber;
        if (dto.QuoteNumber is > 0)
        {
            var exists = await _context.Quotes.AnyAsync(q =>
                q.WorkshopId == workshopId.Value && q.QuoteNumber == dto.QuoteNumber.Value);
            if (exists)
                return Conflict(new { message = $"Ya existe una cotización N.º {dto.QuoteNumber}." });
            quoteNumber = dto.QuoteNumber.Value;
        }
        else
        {
            var nextNumber = await _context.Quotes
                .Where(q => q.WorkshopId == workshopId.Value)
                .Select(q => (long?)q.QuoteNumber)
                .MaxAsync() ?? 0;
            quoteNumber = nextNumber + 1;
        }

        var quote = new Quote
        {
            WorkshopId = workshopId.Value,
            QuoteNumber = quoteNumber,
            QuoteDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ValidUntil = dto.ValidUntil,
            ClientName = dto.ClientName.Trim(),
            ClientEmail = dto.ClientEmail?.Trim() ?? "",
            ClientPhone = dto.ClientPhone?.Trim() ?? "",
            VehicleInfo = dto.VehicleInfo?.Trim() ?? "",
            Notes = dto.Notes?.Trim() ?? "",
            ShareToken = GenerateShareToken()
        };

        ApplyItemsAndTotals(quote, dto);

        _context.Quotes.Add(quote);
        await _context.SaveChangesAsync();

        return Ok(ToDto(quote));
    }

    // PUT /quotes/{id} — solo mientras esté pendiente
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuoteDto>> Update(Guid id, [FromBody] QuoteCreateDto dto)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var quote = await _context.Quotes
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == id && q.WorkshopId == workshopId.Value);

        if (quote == null) return NotFound(new { message = "Cotización no encontrada." });
        if (quote.Status != "pending")
            return Conflict(new { message = "No se puede editar una cotización ya decidida." });

        if (string.IsNullOrWhiteSpace(dto.ClientName))
            return BadRequest(new { message = "El nombre del cliente es requerido." });
        if (dto.Items.Count == 0)
            return BadRequest(new { message = "La cotización debe tener al menos un ítem." });
        if (dto.Items.Any(i => string.IsNullOrWhiteSpace(i.Description) || i.Quantity <= 0 || i.UnitPrice < 0))
            return BadRequest(new { message = "Todos los ítems requieren descripción, cantidad positiva y precio válido." });
        if (dto.TaxRate is < 0 or > 100)
            return BadRequest(new { message = "Tasa de impuesto inválida." });

        // Permitir cambiar el número, validando que no choque con otra cotización
        if (dto.QuoteNumber is > 0 && dto.QuoteNumber.Value != quote.QuoteNumber)
        {
            var exists = await _context.Quotes.AnyAsync(q =>
                q.WorkshopId == workshopId.Value &&
                q.QuoteNumber == dto.QuoteNumber.Value &&
                q.Id != id);
            if (exists)
                return Conflict(new { message = $"Ya existe una cotización N.º {dto.QuoteNumber}." });
            quote.QuoteNumber = dto.QuoteNumber.Value;
        }

        quote.ClientName = dto.ClientName.Trim();
        quote.ClientEmail = dto.ClientEmail?.Trim() ?? "";
        quote.ClientPhone = dto.ClientPhone?.Trim() ?? "";
        quote.VehicleInfo = dto.VehicleInfo?.Trim() ?? "";
        quote.Notes = dto.Notes?.Trim() ?? "";
        quote.ValidUntil = dto.ValidUntil;

        _context.QuoteItems.RemoveRange(quote.Items);
        quote.Items.Clear();
        ApplyItemsAndTotals(quote, dto);

        await _context.SaveChangesAsync();
        return Ok(ToDto(quote));
    }

    // POST /quotes/{id}/approve — aprobación digital con un click (admin)
    [HttpPost("{id:guid}/approve")]
    public Task<IActionResult> Approve(Guid id) => Decide(id, "approved");

    // POST /quotes/{id}/reject
    [HttpPost("{id:guid}/reject")]
    public Task<IActionResult> Reject(Guid id) => Decide(id, "rejected");

    private async Task<IActionResult> Decide(Guid id, string status)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var quote = await _context.Quotes
            .Include(q => q.Items)
            .FirstOrDefaultAsync(q => q.Id == id && q.WorkshopId == workshopId.Value);

        if (quote == null) return NotFound(new { message = "Cotización no encontrada." });
        if (quote.Status != "pending")
            return Conflict(new { message = "La cotización ya fue decidida." });

        quote.Status = status;
        quote.DecidedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(ToDto(quote));
    }

    // DELETE /quotes/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var quote = await _context.Quotes
            .FirstOrDefaultAsync(q => q.Id == id && q.WorkshopId == workshopId.Value);

        if (quote == null) return NotFound(new { message = "Cotización no encontrada." });

        _context.Quotes.Remove(quote);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // GET /quotes/public/{token} — vista pública inmutable, solo lectura
    [HttpGet("public/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicQuoteDto>> GetPublic(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64)
            return NotFound(new { message = "Cotización no encontrada." });

        var quote = await _context.Quotes
            .Include(q => q.Items)
            .Include(q => q.Workshop)
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.ShareToken == token);

        if (quote == null) return NotFound(new { message = "Cotización no encontrada." });

        return Ok(new PublicQuoteDto
        {
            QuoteNumber = quote.QuoteNumber,
            QuoteDate = quote.QuoteDate,
            ValidUntil = quote.ValidUntil,
            ClientName = quote.ClientName,
            VehicleInfo = quote.VehicleInfo,
            Notes = quote.Notes,
            Subtotal = quote.Subtotal,
            Tax = quote.Tax,
            Total = quote.Total,
            Items = quote.Items
                .OrderBy(i => i.Position)
                .Select(ToItemDto)
                .ToList(),
            WorkshopName = quote.Workshop?.WorkshopName ?? "",
            WorkshopLogo = quote.Workshop?.Logo,
            WorkshopDescription = quote.Workshop?.BusinessDescription,
            WorkshopPhone = quote.Workshop?.Phone,
            WorkshopAddress = quote.Workshop?.Address,
            WorkshopEmail = quote.Workshop?.Email,
            WorkshopRuc = quote.Workshop?.Ruc,
            WorkshopDv = quote.Workshop?.Dv
        });
    }

    private static void ApplyItemsAndTotals(Quote quote, QuoteCreateDto dto)
    {
        var position = 0;
        decimal subtotal = 0;
        foreach (var item in dto.Items)
        {
            var lineTotal = Math.Round(item.Quantity * item.UnitPrice, 2);
            subtotal += lineTotal;
            quote.Items.Add(new QuoteItem
            {
                Position = position++,
                Description = item.Description.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                LineTotal = lineTotal
            });
        }

        quote.Subtotal = Math.Round(subtotal, 2);
        quote.Tax = Math.Round(subtotal * (dto.TaxRate / 100m), 2);
        quote.Total = quote.Subtotal + quote.Tax;
    }

    private static string GenerateShareToken()
    {
        // 24 bytes → 32 chars base64url, imposible de adivinar
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static QuoteItemDto ToItemDto(QuoteItem i) => new()
    {
        Id = i.Id,
        Position = i.Position,
        Description = i.Description,
        Quantity = i.Quantity,
        UnitPrice = i.UnitPrice,
        LineTotal = i.LineTotal
    };

    private static QuoteDto ToDto(Quote q) => new()
    {
        Id = q.Id,
        QuoteNumber = q.QuoteNumber,
        QuoteDate = q.QuoteDate,
        ValidUntil = q.ValidUntil,
        ClientName = q.ClientName,
        ClientEmail = q.ClientEmail,
        ClientPhone = q.ClientPhone,
        VehicleInfo = q.VehicleInfo,
        Notes = q.Notes,
        Status = q.Status,
        ShareToken = q.ShareToken,
        Subtotal = q.Subtotal,
        Tax = q.Tax,
        Total = q.Total,
        CreatedAt = q.CreatedAt,
        DecidedAt = q.DecidedAt,
        Items = q.Items.OrderBy(i => i.Position).Select(ToItemDto).ToList()
    };
}
