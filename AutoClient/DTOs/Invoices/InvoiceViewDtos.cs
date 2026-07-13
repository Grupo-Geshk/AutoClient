namespace AutoClient.DTOs.Invoices;

// Fila del historial de facturas (misma idea que QuoteSummaryDto)
public record InvoiceSummaryDto(
    Guid id,
    long invoiceNumber,
    DateOnly invoiceDate,
    string clientName,
    string paymentType,
    decimal total,
    int itemCount,
    DateTime createdAt,
    Guid? serviceId
);

public record InvoiceItemViewDto(
    Guid id,
    decimal quantity,
    string description,
    decimal unitPrice,
    decimal lineTotal,
    int sortOrder
);

// Detalle completo para revisar una factura ya emitida
public record InvoiceDetailDto(
    Guid id,
    long invoiceNumber,
    DateOnly invoiceDate,
    string clientName,
    string clientEmail,
    string clientAddress,
    string paymentType,
    string receivedBy,
    string notes,
    decimal subtotal,
    decimal tax,
    decimal total,
    string pdfUrl,
    DateTime createdAt,
    Guid? serviceId,
    List<InvoiceItemViewDto> items
);
