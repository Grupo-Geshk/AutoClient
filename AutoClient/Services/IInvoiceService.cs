using AutoClient.DTOs.Invoices;

namespace AutoClient.Services;

public interface IInvoiceService
{
    Task<InvoiceResultDto> CreateAsync(InvoiceCreateDto dto, CancellationToken ct = default);
    Task<InvoiceResultDto> CreateFromServiceAsync(Guid serviceId, InvoiceCreateDto? overrides, CancellationToken ct = default);
    Task<Stream> GetPdfStreamAsync(Guid invoiceId, CancellationToken ct = default);
}
