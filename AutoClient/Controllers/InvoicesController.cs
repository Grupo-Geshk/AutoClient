using AutoClient.DTOs.Invoices;
using AutoClient.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoClient.Controllers;

[ApiController]
[Route("invoices")]
[Authorize]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _svc;
    public InvoicesController(IInvoiceService svc) => _svc = svc;

    [HttpPost]
    public async Task<ActionResult<InvoiceResultDto>> Create([FromBody] InvoiceCreateDto dto, CancellationToken ct)
        => Ok(await _svc.CreateAsync(dto, ct));

    [HttpPost("from-service/{serviceId:guid}")]
    public async Task<ActionResult<InvoiceResultDto>> CreateFromService(Guid serviceId, [FromBody] InvoiceCreateDto? overrides, CancellationToken ct)
        => Ok(await _svc.CreateFromServiceAsync(serviceId, overrides, ct));

    [HttpGet("{id:guid}/pdf")]
    [AllowAnonymous] // si quieres permitir descarga pública
    public async Task<IActionResult> GetPdf(Guid id, CancellationToken ct)
    {
        var stream = await _svc.GetPdfStreamAsync(id, ct);
        return File(stream, "application/pdf", enableRangeProcessing: true);
    }
}
