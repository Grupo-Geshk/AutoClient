using AutoClient.DTOs.Invoices;

namespace AutoClient.Services;

public interface IInvoiceService
{
    Task<InvoiceResultDto> CreateAsync(InvoiceCreateDto dto, Guid? workshopId = null, CancellationToken ct = default);
    Task<InvoiceResultDto> CreateFromServiceAsync(Guid serviceId, InvoiceCreateDto? overrides, Guid? workshopId = null, CancellationToken ct = default);
    Task<Stream> GetPdfStreamAsync(Guid invoiceId, CancellationToken ct = default);
    Task<List<InvoiceSummaryDto>> ListAsync(Guid workshopId, string? paymentType, string? search, CancellationToken ct = default);
    Task<InvoiceDetailDto?> GetAsync(Guid invoiceId, Guid workshopId, CancellationToken ct = default);
}
