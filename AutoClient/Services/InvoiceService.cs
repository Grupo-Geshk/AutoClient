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
using AutoClient.Services.Email;

namespace AutoClient.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<InvoiceService> _log;
    private readonly SmtpSettings _smtp;
    private readonly IInvoiceMailer _mailer;

    public InvoiceService(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IOptions<SmtpSettings> smtpOptions,
        ILogger<InvoiceService> log,
        IInvoiceMailer mailer)
    {
        _db = db;
        _env = env;
        _smtp = smtpOptions.Value;
        _log = log;
        _mailer = mailer;

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

        var receivedByDefault = string.Empty;

        var dto = overrides is null
            ? new InvoiceCreateDto(
                template: "styled", // usamos el layout pintado
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

        // 3) items + totales
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

        // 4) PDF: respetar el template solicitado
        byte[] pdfBytes = (dto.template ?? "styled").ToLowerInvariant() switch
        {
            "preprinted" => await RenderPreprintedAsync(inv),
            "digital" => await RenderDigitalAsync(inv),
            _ => await RenderStyledAsync(inv) // "styled" por defecto
        };


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
                var vm = new InvoiceEmailView
                {
                    InvoiceNumber = inv.InvoiceNumber.ToString(),
                    ClientName = inv.ClientName,
                    ClientEmail = inv.ClientEmail,
                    ClientAddress = inv.ClientAddress,
                    Day = dto.date.day,
                    Month = dto.date.month,
                    Year = dto.date.year,
                    PaymentType = dto.paymentType,
                    ReceivedBy = dto.receivedBy,
                    TaxRate = dto.taxRate,
                    Items = inv.Items.Select(it => new InvoiceEmailItem
                    {
                        Qty = it.Quantity,
                        Description = it.Description,
                        UnitPrice = it.UnitPrice
                    }).ToList(),
                    Subtotal = inv.Subtotal,
                    Tax = inv.Tax,
                    Total = inv.Total
                };

                await _mailer.SendAsync(vm, pdfBytes, true, ct);
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

        var bytes = await RenderStyledAsync(inv);
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
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _log.LogWarning("Secuencia 'invoice_number_seq' no encontrada. Creando automáticamente...");
            var createCmdText = "CREATE SEQUENCE invoice_number_seq START 1 INCREMENT 1;";
            await using var createCmd = new NpgsqlCommand(createCmdText, conn);
            await createCmd.ExecuteNonQueryAsync(ct);

            await using var nextCmd = new NpgsqlCommand("SELECT nextval('invoice_number_seq')", conn);
            var result = await nextCmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result);
        }
    }

    // ========= Layout “pintado” tipo talonario (CORREGIDO) =========
    private Task<byte[]> RenderStyledAsync(Invoice inv)
    {
        // Colores
        var Navy = "#1F3B6F"; // azul cabecera / total
        var Navy70 = "#2A4A84";
        var Beige = "#FAF7F2"; // papel
        var Ink = "#0F172A";
        var RedNo = "#C23B2A";

        // Estilos
        var baseText = TextStyle.Default
            .FontSize(12)
            .FontColor(Ink)
            .FontFamily("Helvetica"); // usa el nombre de la fuente como string

        var h1 = baseText
            .Size(22)
            .SemiBold()
            .FontColor(Navy);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(28);
                page.DefaultTextStyle(baseText);

                // si esto te daba error, puedes comentarlo sin problema
                // page.Background().Color(Beige);

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    // CABECERA
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Row(r =>
                        {
                            // Logo opcional: wwwroot/templates/diogenes_logo.png
                            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                            var logoPath = Path.Combine(webRoot, "templates", "diogenes_logo.png");
                            if (System.IO.File.Exists(logoPath))
                                r.ConstantItem(64).Height(64).Image(System.IO.File.ReadAllBytes(logoPath)).FitArea();

                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("AUTO SERVICIOS DIÓGENES").Style(h1);
                                c.Item().Text("R.U.C. 4-246-714  D.V. 18").SemiBold().FontColor(Navy);
                                c.Item().Text("Ventas al por menor de partes, piezas accesorios de vehículos y automotores")
                                    .FontSize(10).FontColor(Navy);
                                c.Item().Text("Los Anastacios, Urbanización Rincón Largo · Calle Principal, Casa 2X · Cel. 6622-4854")
                                    .FontSize(10).FontColor(Navy);
                            });
                        });

                        // Día / Mes / Año + tipo pago
                        row.ConstantItem(220).Column(c =>
                        {
                            c.Item().Row(r =>
                            {
                                void Box(string label, string value)
                                {
                                    r.RelativeItem().Column(cc =>
                                    {
                                        cc.Item().Border(1).BorderColor(Navy).Padding(4).AlignCenter().Text(label).FontSize(9).FontColor(Navy);
                                        cc.Item().Border(1).BorderColor(Navy).Padding(6).AlignCenter().Text(value).SemiBold().FontColor(Navy);
                                    });
                                    r.Spacing(6);
                                }

                                Box("DÍA", inv.InvoiceDate.Day.ToString("00"));
                                Box("MES", inv.InvoiceDate.Month.ToString("00"));
                                Box("AÑO", inv.InvoiceDate.Year.ToString());
                            });

                            c.Item().PaddingTop(8).Row(r =>
                            {
                                void Check(string label, bool on)
                                {
                                    r.ConstantItem(16).Height(16).Border(2).BorderColor(Navy)
                                     .Background(on ? Navy : null);
                                    r.Spacing(6);
                                    r.RelativeItem().AlignMiddle().Text(label).FontSize(11).SemiBold().FontColor(Navy);
                                }

                                Check("CRÉDITO", string.Equals(inv.PaymentType, "credito", StringComparison.OrdinalIgnoreCase));
                                r.Spacing(12);
                                Check("CONTADO", !string.Equals(inv.PaymentType, "credito", StringComparison.OrdinalIgnoreCase));
                            });
                        });
                    });

                    // FACTURA ____   Nº (rojo)
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("FACTURA").SemiBold().FontSize(16).FontColor(Navy);
                        r.Spacing(8);
                        r.RelativeItem().LineHorizontal(1).LineColor(Navy);
                        r.Spacing(12);
                        r.ConstantItem(80).AlignRight().Text(inv.InvoiceNumber.ToString()).FontSize(16).Bold().FontColor(RedNo);
                    });

                    // Cliente / Dirección
                    col.Item().Column(c =>
                    {
                        c.Spacing(4);
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(90).Text("Cliente:").SemiBold();
                            r.RelativeItem().BorderBottom(1).BorderColor(Navy).PaddingBottom(4)
                                .Text(inv.ClientName ?? "");
                        });
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(90).Text("Dirección:").SemiBold();
                            r.RelativeItem().BorderBottom(1).BorderColor(Navy).PaddingBottom(4)
                                .Text(inv.ClientAddress ?? "");
                        });
                    });

                    // TABLA
                    col.Item().Element(elem =>
                    {
                        elem.Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(70);
                                c.RelativeColumn(1);
                                c.ConstantColumn(110);
                                c.ConstantColumn(110);
                            });

                            // header
                            // reemplaza la versión actual
                            t.Header(h =>
                            {
                                void Head(string s) =>
                                    h.Cell()
                                     .Background(Navy)
                                     .Padding(6)
                                     .AlignCenter()                 // <— al contenedor
                                     .Text(s).FontColor("#FFFFFF").SemiBold();  // <— luego el texto

                                Head("CANT.");
                                Head("DESCRIPCION");
                                Head("P. UNIT.");
                                Head("TOTAL");
                            });


                            var rows = inv.Items.OrderBy(i => i.SortOrder).ToList();
                            var maxRows = Math.Max(rows.Count, 14);
                            for (int i = 0; i < maxRows; i++)
                            {
                                var it = i < rows.Count ? rows[i] : null;

                                t.Cell().Border(1).BorderColor(Navy).Padding(6).AlignRight()
                                    .Text(it != null ? it.Quantity.ToString("0.##") : "");
                                t.Cell().Border(1).BorderColor(Navy).Padding(6)
                                    .Text(it?.Description ?? "");
                                t.Cell().Border(1).BorderColor(Navy).Padding(6).AlignRight()
                                    .Text(it != null ? it.UnitPrice.ToString("0.00") : "");
                                t.Cell().Border(1).BorderColor(Navy).Padding(6).AlignRight()
                                    .Text(it != null ? it.LineTotal.ToString("0.00") : "");
                            }
                        });
                    });

                    // TOTALES + Recibido por
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().PaddingTop(20).Column(c =>
                        {
                            c.Item().Text("Recibido por").SemiBold().FontColor(Ink);
                            c.Item().Row(rr => { rr.RelativeItem().BorderBottom(1).BorderColor(Navy).PaddingBottom(4); });
                        });

                        r.ConstantItem(300).Column(c =>
                        {
                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Border(1).BorderColor(Navy).Background("#FFFFFF")
                                    .Padding(8).Text("SUB-TOTAL").SemiBold().FontColor(Ink);
                                rr.ConstantItem(130).Border(1).BorderColor(Navy)
                                    .Padding(8).AlignRight().Text(inv.Subtotal.ToString("0.00")).SemiBold().FontColor(Ink);
                            });

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Border(1).BorderColor(Navy).Background("#FFFFFF")
                                    .Padding(8).Text("I.T.B.M.S").SemiBold().FontColor(Ink);
                                rr.ConstantItem(130).Border(1).BorderColor(Navy)
                                    .Padding(8).AlignRight().Text(inv.Tax.ToString("0.00")).SemiBold().FontColor(Ink);
                            });

                            c.Item().Row(rr =>
                            {
                                rr.RelativeItem().Border(1).BorderColor(Navy).Background(Navy70)
                                    .Padding(10).Text("TOTAL").Bold().FontColor("#FFFFFF");
                                rr.ConstantItem(130).Border(1).BorderColor(Navy).Background(Navy)
                                    .Padding(10).AlignRight().Text(inv.Total.ToString("0.00")).Bold().FontColor("#FFFFFF");
                            });
                        });
                    });
                });
            });
        }).GeneratePdf();   // <= devuelve byte[]

        return Task.FromResult(bytes);
    }



    // ========= Digital simple (queda por si lo quieres seguir usando) =========
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
                            c.RelativeColumn(1);
                            c.RelativeColumn(6);
                            c.RelativeColumn(2);
                            c.RelativeColumn(2);
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

    // ========= (Opcional) con PNG de fondo =========
    private async Task<byte[]> RenderPreprintedAsync(Invoice inv)
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var templatePath = Path.Combine(webRoot, "templates", "diogenes_form.png");

        if (!System.IO.File.Exists(templatePath))
            return await RenderStyledAsync(inv); // si no hay imagen, usa el pintado

        var bg = await System.IO.File.ReadAllBytesAsync(templatePath);

        const float PX = 1f;
        const float TOP = 0f;

        const string BLUE = "#1F3B6F";
        const string RED = "#C23B2A";

        float FACT_NO_X = 140, FACT_NO_Y = 180;
        float DATE_D_X = 535, DATE_D_Y = 175, DATE_M_X = 575, DATE_M_Y = 175, DATE_A_X = 615, DATE_A_Y = 175;
        float CLIENT_X = 84, CLIENT_Y = 230, ADDR_X = 84, ADDR_Y = 262;
        float CREDIT_X = 560, CREDIT_Y = 236, CASH_X = 560, CASH_Y = 264;
        float ROWS_START_Y = 318, ROW_HEIGHT = 28, COL_QTY_X = 68, COL_DESC_X = 150, COL_UNIT_X = 470, COL_TOTAL_X = 560;
        int MAX_ROWS = 16;
        float SUBT_X = 560, SUBT_Y = 685, ITBMS_X = 560, ITBMS_Y = 712, TOT_X = 560, TOT_Y = 740;
        float REC_X = 110, REC_Y = 760;

        try
        {
            var bytes = Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.Letter);
                    page.Margin(0);
                    page.DefaultTextStyle(t => t.FontSize(11));

                    page.Background().Image(bg).FitArea();

                    page.Content().Element(root =>
                    {
                        root.Element(e => e.TranslateX(FACT_NO_X * PX).TranslateY((FACT_NO_Y + TOP) * PX)
                            .Text(inv.InvoiceNumber.ToString()).FontSize(16).Bold().FontColor(RED));

                        root.Element(e => e.TranslateX(DATE_D_X * PX).TranslateY((DATE_D_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Day.ToString("00")).Bold().FontColor(BLUE));
                        root.Element(e => e.TranslateX(DATE_M_X * PX).TranslateY((DATE_M_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Month.ToString("00")).Bold().FontColor(BLUE));
                        root.Element(e => e.TranslateX(DATE_A_X * PX).TranslateY((DATE_A_Y + TOP) * PX)
                            .Text(inv.InvoiceDate.Year.ToString()).Bold().FontColor(BLUE));

                        root.Element(e => e.TranslateX(CLIENT_X * PX).TranslateY((CLIENT_Y + TOP) * PX)
                            .Text(inv.ClientName ?? "").FontSize(12));
                        root.Element(e => e.TranslateX(ADDR_X * PX).TranslateY((ADDR_Y + TOP) * PX)
                            .Text(inv.ClientAddress ?? "").FontSize(12));

                        bool isCredito = string.Equals(inv.PaymentType, "credito", StringComparison.OrdinalIgnoreCase);
                        root.Element(e => e.TranslateX(CREDIT_X * PX).TranslateY((CREDIT_Y + TOP) * PX)
                            .Text(isCredito ? "✔" : "").Bold().FontColor(BLUE));
                        root.Element(e => e.TranslateX(CASH_X * PX).TranslateY((CASH_Y + TOP) * PX)
                            .Text(!isCredito ? "✔" : "").Bold().FontColor(BLUE));

                        int i = 0;
                        foreach (var it in inv.Items.OrderBy(x => x.SortOrder).Take(MAX_ROWS))
                        {
                            var y = (ROWS_START_Y + i * ROW_HEIGHT + TOP) * PX;
                            root.Element(e => e.TranslateX(COL_QTY_X * PX).TranslateY(y)
                                .Text(it.Quantity.ToString("0.##")).FontSize(12));
                            root.Element(e => e.TranslateX(COL_DESC_X * PX).TranslateY(y)
                                .Text(it.Description ?? "").FontSize(12));
                            root.Element(e => e.TranslateX(COL_UNIT_X * PX).TranslateY(y)
                                .Text(it.UnitPrice.ToString("0.00")).FontSize(12));
                            root.Element(e => e.TranslateX(COL_TOTAL_X * PX).TranslateY(y)
                                .Text(it.LineTotal.ToString("0.00")).FontSize(12));
                            i++;
                        }

                        root.Element(e => e.TranslateX(SUBT_X * PX).TranslateY((SUBT_Y + TOP) * PX)
                            .Text(inv.Subtotal.ToString("0.00")).Bold().FontColor(BLUE));
                        root.Element(e => e.TranslateX(ITBMS_X * PX).TranslateY((ITBMS_Y + TOP) * PX)
                            .Text(inv.Tax.ToString("0.00")).Bold().FontColor(BLUE));
                        root.Element(e => e.TranslateX(TOT_X * PX).TranslateY((TOT_Y + TOP) * PX)
                            .Text(inv.Total.ToString("0.00")).Bold().FontColor(BLUE));

                        root.Element(e => e.TranslateX(REC_X * PX).TranslateY((REC_Y + TOP) * PX)
                            .Text(inv.ReceivedBy ?? "").FontSize(12));
                    });
                });
            }).GeneratePdf();

            return bytes;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "QuestPDF falló en RenderPreprintedAsync.");
            return await RenderStyledAsync(inv);
        }
    }

    // ========= Email “simple” (no se usa si trabajas con IInvoiceMailer) =========
    private async Task SendEmailAsync(Invoice inv, byte[] pdf)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.SenderName ?? "AutoClient", _smtp.SenderEmail));
        message.To.Add(new MailboxAddress(inv.ClientName, inv.ClientEmail));
        message.Bcc.Add(new MailboxAddress("Auto Servicios Diógenes", "zerokay02@gmail.com"));
        message.Subject = $"Factura #{inv.InvoiceNumber} - Auto Servicios Diógenes";

        var builder = new BodyBuilder { TextBody = $"Adjuntamos la factura #{inv.InvoiceNumber}.\nTotal: {inv.Total:0.00}" };
        builder.Attachments.Add($"Factura_{inv.InvoiceNumber}.pdf", pdf, new ContentType("application", "pdf"));
        message.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(_smtp.Host, _smtp.Port, SecureSocketOptions.StartTls);
        await smtp.AuthenticateAsync(_smtp.Username, _smtp.Password);
        await smtp.SendAsync(message);
        await smtp.DisconnectAsync(true);
    }
}
