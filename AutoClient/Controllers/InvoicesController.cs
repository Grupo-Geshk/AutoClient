using AutoClient.DTOs.Invoices;
using AutoClient.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoClient.Controllers;

[ApiController]
[Route("invoices")]
[Authorize]
[Produces("application/json")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _svc;
    private readonly ILogger<InvoicesController> _logger;
    private readonly IWebHostEnvironment _env;

    public InvoicesController(IInvoiceService svc, ILogger<InvoicesController> logger, IWebHostEnvironment env)
    {
        _svc = svc;
        _logger = logger;
        _env = env;
    }

    private Guid? GetWorkshopId()
    {
        var claim = User.FindFirst("workshop_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    // GET /invoices?paymentType=contado|credito&search=
    [HttpGet]
    [ProducesResponseType(typeof(List<InvoiceSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<InvoiceSummaryDto>>> List(
        [FromQuery] string? paymentType,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var invoices = await _svc.ListAsync(workshopId.Value, paymentType, search, ct);
        return Ok(invoices);
    }

    // GET /invoices/{id}
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(InvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<InvoiceDetailDto>> Get(Guid id, CancellationToken ct)
    {
        var workshopId = GetWorkshopId();
        if (workshopId == null) return Unauthorized(new { message = "Invalid token." });

        var invoice = await _svc.GetAsync(id, workshopId.Value, ct);
        if (invoice == null) return NotFound(new { message = "Factura no encontrada." });
        return Ok(invoice);
    }

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(InvoiceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InvoiceResultDto>> Create([FromBody] InvoiceCreateDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        _logger.LogInformation(
            "Creating invoice. Client: {ClientName}, SendEmail: {SendEmail}, Email: {ClientEmail}, Items: {ItemCount}",
            dto.client?.name ?? "(none)",
            dto.sendEmail,
            string.IsNullOrWhiteSpace(dto.client?.email) ? "(none)" : dto.client.email,
            dto.items?.Count ?? 0);

        try
        {
            var res = await _svc.CreateAsync(dto, GetWorkshopId(), ct);
            _logger.LogInformation(
                "Invoice created successfully. Number: {InvoiceNumber}, Id: {InvoiceId}, PdfUrl: {PdfUrl}",
                res.number, res.id, res.pdfUrl);
            return Ok(res);
        }
        catch (InvalidOperationException ex)
        {
            // Validación de negocio (p. ej. número de factura duplicado)
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando factura");
            if (_env.IsDevelopment())
                return Problem(title: "Error creando factura", detail: ex.ToString(), statusCode: 500);
            return Problem("Error creando factura", statusCode: 500);
        }
    }

    [HttpPost("from-service/{serviceId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(InvoiceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InvoiceResultDto>> CreateFromService(Guid serviceId, [FromBody] InvoiceCreateDto? overrides, CancellationToken ct)
    {
        _logger.LogInformation(
            "Creating invoice from service. ServiceId: {ServiceId}, HasOverrides: {HasOverrides}",
            serviceId, overrides != null);

        try
        {
            var res = await _svc.CreateFromServiceAsync(serviceId, overrides, GetWorkshopId(), ct);
            _logger.LogInformation(
                "Invoice from service created successfully. ServiceId: {ServiceId}, Number: {InvoiceNumber}, Id: {InvoiceId}",
                serviceId, res.number, res.id);
            return Ok(res);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando factura desde servicio {ServiceId}", serviceId);
            if (_env.IsDevelopment())
                return Problem(title: "Error creando factura desde servicio", detail: ex.ToString(), statusCode: 500);
            return Problem("Error creando factura desde servicio", statusCode: 500);
        }
    }

    [HttpGet("{id:guid}/pdf")]
    [AllowAnonymous] // quítalo si no quieres descarga pública
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken ct)
    {
        try
        {
            var stream = await _svc.GetPdfStreamAsync(id, ct);
            return File(stream, "application/pdf", enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo PDF de la factura {InvoiceId}", id);
            if (_env.IsDevelopment())
                return Problem(title: "Error obteniendo PDF", detail: ex.ToString(), statusCode: 500);
            return Problem("Error obteniendo PDF", statusCode: 500);
        }
    }
}
