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

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(InvoiceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InvoiceResultDto>> Create([FromBody] InvoiceCreateDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        try
        {
            var res = await _svc.CreateAsync(dto, ct);
            return Ok(res);
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
        try
        {
            var res = await _svc.CreateFromServiceAsync(serviceId, overrides, ct);
            return Ok(res);
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
