namespace AutoClient.DTOs.Invoices;

public record InvoiceCreateDto(
    string template,                  // "preprinted" | "digital"
    ClientDto client,
    InvoiceDateDto date,
    string paymentType,               // "contado" | "credito"
    string receivedBy,
    List<InvoiceItemDto> items,
    decimal taxRate,                  // 0.07
    bool sendEmail,
    Guid? serviceId                   // opcional
);

public record ClientDto(string name, string email, string address);

public record InvoiceDateDto(int day, int month, int year);

public record InvoiceItemDto(decimal qty, string description, decimal unitPrice);

public record InvoiceResultDto(Guid id, long number, string pdfUrl);
