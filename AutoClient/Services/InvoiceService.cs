using AutoClient.Data;
using AutoClient.DTOs.Invoices;
using AutoClient.Models;
using AutoClient.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Npgsql;
using System.IO;
using System.Linq;

namespace AutoClient.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<InvoiceService> _log;
    private readonly SmtpSettings _smtp;

    public InvoiceService(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IOptions<SmtpSettings> smtpOptions,
        ILogger<InvoiceService> log)
    {
        _db = db;
        _env = env;
        _smtp = smtpOptions.Value;
        _log = log;

        // Aceptar licencia Community (recomendado también en Program.cs al iniciar la app).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public async Task<InvoiceResultDto> CreateFromServiceAsync(Guid serviceId, InvoiceCreateDto? overrides, CancellationToken ct = default)
    {
        var service = await _db.Services
            .Include(s => s.Vehicle)!.ThenInclude(v => v.Client)
            .FirstOrDefaultAsync(s => s.Id == serviceId, ct)
            ?? throw new Exception("Servicio no existe.");

        if (service.ExitDate is null)
            throw new Exception("El servicio no tiene fecha de salida (ExitDate).");

        var defClient = new ClientDto(
            service.Vehicle!.Client!.Name,
            service.Vehicle!.Client!.Email ?? string.Empty,
            service.Vehicle!.Client!.Address ?? string.Empty
        );

        var defDate = new InvoiceDateDto(
            service.ExitDate.Value.Day,
            service.ExitDate.Value.Month,
            service.ExitDate.Value.Year
        );

        var defItems = new List<InvoiceItemDto>
        {
            new(1, service.ServiceType ?? "Servicio", service.Cost ?? 0m)
        };

        var receivedByDefault = string.Empty; // Ajusta si luego usas Worker/DeliveredBy

        var dto = overrides is null
            ? new InvoiceCreateDto(
                template: "preprinted",
                client: defClient,
                date: defDate,
                paymentType: "contado",
                receivedBy: receivedByDefault,
                items: defItems,
                taxRate: 0.07m,
                sendEmail: true,
                serviceId: serviceId
              )
            : overrides with
            {
                client = overrides.client ?? defClient,
                date = overrides.date ?? defDate,
                items = (overrides.items is { Count: > 0 }) ? overrides.items : defItems,
                serviceId = serviceId
            };

        return await CreateAsync(dto, ct);
    }

    public async Task<InvoiceResultDto> CreateAsync(InvoiceCreateDto dto, CancellationToken ct = default)
    {
        // 1) correlativo
        var nextNumber = await NextInvoiceNumberAsync(ct);

        // 2) map Header
        var date = new DateOnly(dto.date.year, dto.date.month, dto.date.day);
        var inv = new Invoice
        {
            ServiceId = dto.serviceId,
            InvoiceNumber = nextNumber,
            InvoiceDate = date,
            ClientName = dto.client.name ?? string.Empty,
            ClientEmail = dto.client.email ?? string.Empty,
            ClientAddress = dto.client.address ?? string.Empty,
            PaymentType = dto.paymentType ?? "contado",
            ReceivedBy = dto.receivedBy ?? string.Empty
        };

        // 3) map Items + totales
        decimal subtotal = 0m;
        int order = 1;
        foreach (var it in dto.items)
        {
            var lineTotal = Math.Round(it.qty * it.unitPrice, 2, MidpointRounding.AwayFromZero);
            inv.Items.Add(new InvoiceItem
            {
                Quantity = it.qty,
                Description = it.description,
                UnitPrice = it.unitPrice,
                LineTotal = lineTotal,
                SortOrder = order++
            });
            subtotal += lineTotal;
        }

        inv.Subtotal = subtotal;
        inv.Tax = Math.Round(subtotal * dto.taxRate, 2, MidpointRounding.AwayFromZero);
        inv.Total = Math.Round(inv.Subtotal + inv.Tax, 2, MidpointRounding.AwayFromZero);

        _db.Invoices.Add(inv);
        await _db.SaveChangesAsync(ct);

        // 4) Render PDF
        var pdfBytes = string.Equals(dto.template, "preprinted", StringComparison.OrdinalIgnoreCase)
            ? await RenderPreprintedAsync(inv)
            : await RenderDigitalAsync(inv);

        // 5) Guardar archivo
        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var folder = Path.Combine(webRoot, "invoices");
        Directory.CreateDirectory(folder);

        var fileName = $"invoice_{inv.InvoiceNumber}.pdf";
        var path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, pdfBytes, ct);

        inv.PdfUrl = $"/invoices/{fileName}";
        await _db.SaveChangesAsync(ct);

        // 6) Email opcional
        if (dto.sendEmail && !string.IsNullOrWhiteSpace(inv.ClientEmail))
        {
            try
            {
                await SendEmailAsync(inv, pdfBytes);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error enviando correo de factura");
            }
        }

        return new InvoiceResultDto(inv.Id, inv.InvoiceNumber, inv.PdfUrl);
    }

    public async Task<Stream> GetPdfStreamAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new Exception("Factura no encontrada.");

        // Para stream rápido usamos la digital (puedes condicionar si quieres).
        var bytes = await RenderDigitalAsync(inv);
        return new MemoryStream(bytes);
    }

    // ========== CORRELATIVO ==========
    private async Task<long> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var cs = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(cs))
            throw new Exception("Connection string no configurado.");

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(ct);

        try
        {
            await using var cmd = new NpgsqlCommand("SELECT nextval('invoice_number_seq')", conn);
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table / sequence no existe
        {
            // Crear la secuencia en caliente
            _log.LogWarning("Secuencia 'invoice_number_seq' no encontrada. Creando automáticamente...");
            var createCmdText = "CREATE SEQUENCE invoice_number_seq START 1 INCREMENT 1;";
            await using var createCmd = new NpgsqlCommand(createCmdText, conn);
            await createCmd.ExecuteNonQueryAsync(ct);

            // Tomar primer valor
            await using var nextCmd = new NpgsqlCommand("SELECT nextval('invoice_number_seq')", conn);
            var result = await nextCmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
    }


    // ========= Plantillas QuestPDF =========
    private Task<byte[]> RenderDigitalAsync(Invoice inv)
    {
        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(24);
                page.Size(PageSizes.Letter);

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Text("AUTO SERVICIOS DIÓGENES").Bold().FontSize(18);
                    col.Item().Text($"Factura N° {inv.InvoiceNumber} · Fecha: {inv.InvoiceDate:dd/MM/yyyy}");
                    col.Item().Text($"Cliente: {inv.ClientName}");
                    if (!string.IsNullOrWhiteSpace(inv.ClientAddress))
                        col.Item().Text($"Dirección: {inv.ClientAddress}");
                    col.Item().Text($"Pago: {inv.PaymentType.ToUpper()}   Recibido por: {inv.ReceivedBy}");

                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);  // Cant
                            c.RelativeColumn(6);  // Desc
                            c.RelativeColumn(2);  // P.Unit
                            c.RelativeColumn(2);  // Total
                        });

                        t.Header(h =>
                        {
                            h.Cell().Text("CANT.");
                            h.Cell().Text("DESCRIPCIÓN");
                            h.Cell().Text("P. UNIT.");
                            h.Cell().Text("TOTAL");
                        });

                        foreach (var it in inv.Items.OrderBy(i => i.SortOrder))
                        {
                            t.Cell().Text(it.Quantity.ToString("0.##"));
                            t.Cell().Text(it.Description);
                            t.Cell().Text(it.UnitPrice.ToString("0.00"));
                            t.Cell().Text(it.LineTotal.ToString("0.00"));
                        }
                    });

                    col.Item().AlignRight().Column(tot =>
                    {
                        tot.Item().Text($"SUB-TOTAL: {inv.Subtotal:0.00}");
                        tot.Item().Text($"I.T.B.M.S: {inv.Tax:0.00}");
                        tot.Item().Text($"TOTAL: {inv.Total:0.00}").Bold();
                    });
                });
            });
        }).GeneratePdf();

        return Task.FromResult(bytes);
    }

    private async Task<byte[]> RenderPreprintedAsync(Invoice inv)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var templatePath = Path.Combine(webRoot, "templates", "diogenes_form.png");

        if (!File.Exists(templatePath))
            return await RenderDigitalAsync(inv);

        var bg = await File.ReadAllBytesAsync(templatePath);

        // === Coordenadas (pt). Ajusta a tu impresora ===
        const float PX = 1f;      // escala fina global (0.99f / 1.01f si tu impresora descalibra)
        const float TOP = 32f;    // desplazamiento vertical global

        // Encabezado derecha (número y fecha)
        var FACT_NO_X = 420f; var FACT_NO_Y = 68f;
        var DATE_D_X = 510f; var DATE_Y = 96f;
        var DATE_M_X = 550f;
        var DATE_A_X = 590f;

        // Cliente / Dirección
        var CLIENT_X = 70f; var CLIENT_Y = 160f;
        var ADDR_X = 70f; var ADDR_Y = 188f;

        // CRÉDITO / CONTADO
        var CRED_X = 520f; var PAY_Y = 160f;
        var CONT_X = 520f; var CONT_Y = 180f;

        // Tabla de ítems
        var ROWS_START_Y = 230f;
        var ROW_HEIGHT = 26f;
        var COL_QTY_X = 60f;
        var COL_DESC_X = 120f;
        var COL_UNIT_X = 430f;
        var COL_TOTAL_X = 520f;
        var MAX_ROWS = 12;

        // Totales
        var SUBT_X = 520f; var SUBT_Y = 520f;
        var ITBMS_X = 520f; var ITBMS_Y = 546f;
        var TOT_X = 520f; var TOT_Y = 572f;

        // Recibido por
        var REC_X = 90f; var REC_Y = 610f;

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(0);

                page.Content().Layers(l =>
                {
                    // Capa de fondo
                    l.PrimaryLayer().Image(bg);

                    // Capa de escritura: usamos contenedores con Translate (no Canvas)
                    l.Layer().Element(root =>
                    {
                        // FACTURA Nº
                        root.Element(e => e
                            .TranslateX(FACT_NO_X * PX)
                            .TranslateY((FACT_NO_Y + TOP) * PX)
                            .Text($"N° {inv.InvoiceNumber}")
                                .FontSize(14)
                                .Bold()
                                .FontColor("#B23A2A")
                        );

                        // Fecha DÍA / MES / AÑO
                        root.Element(e => e.TranslateX(DATE_D_X * PX).TranslateY((DATE_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Day.ToString("00")).Bold());
                        root.Element(e => e.TranslateX(DATE_M_X * PX).TranslateY((DATE_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Month.ToString("00")).Bold());
                        root.Element(e => e.TranslateX(DATE_A_X * PX).TranslateY((DATE_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Year.ToString()).Bold());

                        // Cliente / Dirección
                        root.Element(e => e.TranslateX(CLIENT_X * PX).TranslateY((CLIENT_Y + TOP) * PX)
                            .Text(inv.ClientName ?? "").FontSize(11));
                        root.Element(e => e.TranslateX(ADDR_X * PX).TranslateY((ADDR_Y + TOP) * PX)
                            .Text(inv.ClientAddress ?? "").FontSize(11));

                        // CRÉDITO / CONTADO (marca ✔)
                        var isCredito = string.Equals(inv.PaymentType, "credito", StringComparison.OrdinalIgnoreCase);
                        var isContado = !isCredito;
                        root.Element(e => e.TranslateX(CRED_X * PX).TranslateY((PAY_Y + TOP) * PX)
                            .Text(isCredito ? "✔" : "").Bold());
                        root.Element(e => e.TranslateX(CONT_X * PX).TranslateY((CONT_Y + TOP) * PX)
                            .Text(isContado ? "✔" : "").Bold());

                        // Filas de ítems
                        int i = 0;
                        foreach (var it in inv.Items.OrderBy(x => x.SortOrder).Take(MAX_ROWS))
                        {
                            var y = (ROWS_START_Y + i * ROW_HEIGHT + TOP) * PX;

                            root.Element(e => e.TranslateX(COL_QTY_X * PX).TranslateY(y)
                                .Text(it.Quantity.ToString("0.##")).FontSize(11));
                            root.Element(e => e.TranslateX(COL_DESC_X * PX).TranslateY(y)
                                .Text(it.Description ?? "").FontSize(11));
                            root.Element(e => e.TranslateX(COL_UNIT_X * PX).TranslateY(y)
                                .Text(it.UnitPrice.ToString("0.00")).FontSize(11));
                            root.Element(e => e.TranslateX(COL_TOTAL_X * PX).TranslateY(y)
                                .Text(it.LineTotal.ToString("0.00")).FontSize(11));

                            i++;
                        }

                        // Totales
                        root.Element(e => e.TranslateX(SUBT_X * PX).TranslateY((SUBT_Y + TOP) * PX)
                            .Text(inv.Subtotal.ToString("0.00")).Bold());
                        root.Element(e => e.TranslateX(ITBMS_X * PX).TranslateY((ITBMS_Y + TOP) * PX)
                            .Text(inv.Tax.ToString("0.00")).Bold());
                        root.Element(e => e.TranslateX(TOT_X * PX).TranslateY((TOT_Y + TOP) * PX)
                            .Text(inv.Total.ToString("0.00")).Bold());

                        // Recibido por
                        root.Element(e => e.TranslateX(REC_X * PX).TranslateY((REC_Y + TOP) * PX)
                            .Text(inv.ReceivedBy ?? "").FontSize(11));
                    });
                });
            });
        }).GeneratePdf();

        return bytes;
    }


    // ========= Email =========
    private async Task SendEmailAsync(Invoice inv, byte[] pdf)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.SenderName ?? "AutoClient", _smtp.SenderEmail));
        message.To.Add(new MailboxAddress(inv.ClientName, inv.ClientEmail));

        // Copia oculta a autoserviciosdiogenes@gmail.com
        message.Bcc.Add(new MailboxAddress("Auto Servicios Diógenes", "zerokay02@gmail.com"));

        message.Subject = $"Factura #{inv.InvoiceNumber} - Auto Servicios Diógenes";

        var builder = new BodyBuilder
        {
            TextBody = $"Adjuntamos la factura #{inv.InvoiceNumber}.\nTotal: {inv.Total:0.00}"
        };

        builder.Attachments.Add($"Factura_{inv.InvoiceNumber}.pdf", pdf, new ContentType("application", "pdf"));
        message.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(_smtp.Username, _smtp.Password);
        await smtp.SendAsync(message);
        await smtp.DisconnectAsync(true);
    }

}
