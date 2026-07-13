using AutoClient.Data;
using AutoClient.DTOs.Invoices;
using AutoClient.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Npgsql;
using AutoClient.Services.Email;

namespace AutoClient.Services;

public class InvoiceService : IInvoiceService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<InvoiceService> _log;
    private readonly IInvoiceMailer _mailer;

    public InvoiceService(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        ILogger<InvoiceService> log,
        IInvoiceMailer mailer)
    {
        _db = db;
        _env = env;
        _log = log;
        _mailer = mailer;
        // OJO: nada de QuestPDF aquí. Tocar QuestPDF.Settings dispara la carga
        // de libSkiaSharp; si las dependencias nativas faltan, la excepción en
        // el constructor tumba por DI todos los endpoints de /invoices.
    }

    // El licenciamiento se resuelve al primer render, no al construir el servicio
    private static void EnsureQuestPdfLicense()
        => QuestPDF.Settings.License = LicenseType.Community;

    // Cliente compartido para descargar el logo del taller al generar PDFs
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task<InvoiceResultDto> CreateFromServiceAsync(Guid serviceId, InvoiceCreateDto? overrides, Guid? workshopId = null, CancellationToken ct = default)
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

        return await CreateAsync(dto, workshopId, ct);
    }

    public async Task<InvoiceResultDto> CreateAsync(InvoiceCreateDto dto, Guid? workshopId = null, CancellationToken ct = default)
    {
        // 0) identidad del taller emisor (perfil configurado por el usuario)
        Workshop? workshop = workshopId.HasValue
            ? await _db.Workshops.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workshopId.Value, ct)
            : null;

        // 1) correlativo
        var nextNumber = await NextInvoiceNumberAsync(ct);

        // 2) map Header
        var date = new DateOnly(dto.date.year, dto.date.month, dto.date.day);
        var inv = new Invoice
        {
            ServiceId = dto.serviceId,
            WorkshopId = workshop?.Id,
            InvoiceNumber = nextNumber,
            InvoiceDate = date,
            ClientName = dto.client.name ?? string.Empty,
            ClientEmail = dto.client.email ?? string.Empty,
            ClientAddress = dto.client.address ?? string.Empty,
            PaymentType = dto.paymentType ?? "contado",
            ReceivedBy = dto.receivedBy ?? string.Empty,
            Notes = dto.notes?.Trim() ?? string.Empty
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

        // 4-5) PDF del servidor: best-effort. La factura ya quedó guardada; si
        // el render falla (p. ej. dependencias nativas de SkiaSharp ausentes)
        // solo se pierde el archivo/adjunto — el front genera su propio PDF.
        byte[]? pdfBytes = null;
        try
        {
            pdfBytes = (dto.template ?? "styled").ToLowerInvariant() switch
            {
                "preprinted" => await RenderPreprintedAsync(inv, workshop),
                "digital" => await RenderDigitalAsync(inv, workshop),
                _ => await RenderStyledAsync(inv, workshop) // "styled" por defecto
            };

            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var folder = Path.Combine(webRoot, "invoices");
            Directory.CreateDirectory(folder);

            var fileName = $"invoice_{inv.InvoiceNumber}.pdf";
            var path = Path.Combine(folder, fileName);
            await File.WriteAllBytesAsync(path, pdfBytes, ct);

            inv.PdfUrl = $"/invoices/{fileName}";
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "No se pudo generar el PDF de la factura {InvoiceNumber}; la factura queda guardada sin PDF del servidor.",
                inv.InvoiceNumber);
        }

        // 6) Email opcional
        if (dto.sendEmail)
        {
            if (string.IsNullOrWhiteSpace(inv.ClientEmail))
            {
                _log.LogWarning(
                    "Email requested but client email is empty. InvoiceNumber: {InvoiceNumber}, ClientName: {ClientName}",
                    inv.InvoiceNumber, inv.ClientName);
            }
            else
            {
                _log.LogInformation(
                    "Sending invoice email. InvoiceNumber: {InvoiceNumber}, Recipient: {ClientEmail}",
                    inv.InvoiceNumber, inv.ClientEmail);

                try
                {
                    var vm = new InvoiceEmailView
                    {
                        WorkshopName = workshop?.WorkshopName,
                        WorkshopRuc = workshop?.Ruc,
                        WorkshopDv = workshop?.Dv,
                        WorkshopDescription = workshop?.BusinessDescription,
                        WorkshopLogo = workshop?.Logo,
                        WorkshopNotificationEmail = workshop?.NotificationEmail,
                        InvoiceNumber = inv.InvoiceNumber.ToString(),
                        ClientName = inv.ClientName,
                        ClientEmail = inv.ClientEmail,
                        ClientAddress = inv.ClientAddress,
                        Day = dto.date.day,
                        Month = dto.date.month,
                        Year = dto.date.year,
                        PaymentType = dto.paymentType,
                        ReceivedBy = dto.receivedBy,
                        Notes = inv.Notes,
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

                    await _mailer.SendAsync(vm, pdfBytes ?? Array.Empty<byte>(), true, ct);
                    _log.LogInformation(
                        "Invoice email sent successfully. InvoiceNumber: {InvoiceNumber}, Recipient: {ClientEmail}",
                        inv.InvoiceNumber, inv.ClientEmail);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex,
                        "Failed to send invoice email. InvoiceNumber: {InvoiceNumber}, Recipient: {ClientEmail}",
                        inv.InvoiceNumber, inv.ClientEmail);
                }
            }
        }
        else
        {
            _log.LogInformation(
                "Email not requested for invoice. InvoiceNumber: {InvoiceNumber}",
                inv.InvoiceNumber);
        }

        return new InvoiceResultDto(inv.Id, inv.InvoiceNumber, inv.PdfUrl);
    }

    // Historial: facturas del taller (las legadas sin WorkshopId también se
    // incluyen para no perder el histórico anterior a la migración)
    public async Task<List<InvoiceSummaryDto>> ListAsync(Guid workshopId, string? paymentType, string? search, CancellationToken ct = default)
    {
        var query = _db.Invoices.AsNoTracking()
            .Where(i => i.WorkshopId == workshopId || i.WorkshopId == null);

        if (!string.IsNullOrWhiteSpace(paymentType))
            query = query.Where(i => i.PaymentType == paymentType);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i =>
                EF.Functions.ILike(i.ClientName, $"%{term}%") ||
                i.InvoiceNumber.ToString().Contains(term));
        }

        return await query
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => new InvoiceSummaryDto(
                i.Id,
                i.InvoiceNumber,
                i.InvoiceDate,
                i.ClientName,
                i.PaymentType,
                i.Total,
                i.Items.Count,
                i.CreatedAt,
                i.ServiceId))
            .ToListAsync(ct);
    }

    public async Task<InvoiceDetailDto?> GetAsync(Guid invoiceId, Guid workshopId, CancellationToken ct = default)
    {
        var inv = await _db.Invoices.AsNoTracking()
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId && (i.WorkshopId == workshopId || i.WorkshopId == null), ct);

        if (inv is null) return null;

        return new InvoiceDetailDto(
            inv.Id,
            inv.InvoiceNumber,
            inv.InvoiceDate,
            inv.ClientName,
            inv.ClientEmail,
            inv.ClientAddress,
            inv.PaymentType,
            inv.ReceivedBy,
            inv.Notes,
            inv.Subtotal,
            inv.Tax,
            inv.Total,
            inv.PdfUrl,
            inv.CreatedAt,
            inv.ServiceId,
            inv.Items
                .OrderBy(it => it.SortOrder)
                .Select(it => new InvoiceItemViewDto(
                    it.Id, it.Quantity, it.Description, it.UnitPrice, it.LineTotal, it.SortOrder))
                .ToList());
    }

    public async Task<Stream> GetPdfStreamAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await _db.Invoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new Exception("Factura no encontrada.");

        Workshop? workshop = inv.WorkshopId.HasValue
            ? await _db.Workshops.AsNoTracking().FirstOrDefaultAsync(w => w.Id == inv.WorkshopId.Value, ct)
            : null;

        var bytes = await RenderStyledAsync(inv, workshop);
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

    // ========= Identidad de marca para los documentos =========
    // Se resuelve desde el perfil del taller (Configuración). Facturas
    // antiguas sin taller asociado conservan el encabezado legado.
    private sealed record BrandInfo(string Name, string RucLine, string Description, string ContactLine, byte[]? LogoBytes);

    private async Task<BrandInfo> ResolveBrandAsync(Workshop? ws)
    {
        if (ws is null)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var logoPath = Path.Combine(webRoot, "templates", "diogenes_logo.png");
            byte[]? legacyLogo = System.IO.File.Exists(logoPath)
                ? await System.IO.File.ReadAllBytesAsync(logoPath)
                : null;

            return new BrandInfo(
                "AUTO SERVICIOS DIÓGENES",
                "R.U.C. 4-246-714  D.V. 18",
                "Ventas al por menor de partes, piezas accesorios de vehículos y automotores",
                "Los Anastacios, Urbanización Rincón Largo · Calle Principal, Casa 2X · Cel. 6622-4854",
                legacyLogo);
        }

        var rucLine = string.IsNullOrWhiteSpace(ws.Ruc)
            ? ""
            : $"R.U.C. {ws.Ruc}" + (string.IsNullOrWhiteSpace(ws.Dv) ? "" : $"  D.V. {ws.Dv}");

        var contactParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ws.Address)) contactParts.Add(ws.Address);
        if (!string.IsNullOrWhiteSpace(ws.Phone)) contactParts.Add($"Tel. {ws.Phone}");

        byte[]? logo = null;
        if (!string.IsNullOrWhiteSpace(ws.Logo))
        {
            try
            {
                logo = await _http.GetByteArrayAsync(ws.Logo);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo descargar el logo del taller {WorkshopId}", ws.Id);
            }
        }

        return new BrandInfo(
            ws.WorkshopName.ToUpperInvariant(),
            rucLine,
            ws.BusinessDescription ?? "",
            string.Join(" · ", contactParts),
            logo);
    }

    // ========= Layout “pintado” tipo talonario (CORREGIDO) =========
    private async Task<byte[]> RenderStyledAsync(Invoice inv, Workshop? ws)
    {
        EnsureQuestPdfLicense();
        var brand = await ResolveBrandAsync(ws);
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
                            // Logo del perfil del taller (o legado si no hay taller)
                            if (brand.LogoBytes is { Length: > 0 })
                                r.ConstantItem(64).Height(64).Image(brand.LogoBytes).FitArea();

                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(brand.Name).Style(h1);
                                if (!string.IsNullOrWhiteSpace(brand.RucLine))
                                    c.Item().Text(brand.RucLine).SemiBold().FontColor(Navy);
                                if (!string.IsNullOrWhiteSpace(brand.Description))
                                    c.Item().Text(brand.Description).FontSize(10).FontColor(Navy);
                                if (!string.IsNullOrWhiteSpace(brand.ContactLine))
                                    c.Item().Text(brand.ContactLine).FontSize(10).FontColor(Navy);
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

                    // NOTAS para el cliente (opcional)
                    if (!string.IsNullOrWhiteSpace(inv.Notes))
                    {
                        col.Item().PaddingTop(6).Column(c =>
                        {
                            c.Item().Text("NOTAS").SemiBold().FontColor(Navy);
                            c.Item().Border(1).BorderColor(Navy).Padding(8)
                                .Text(inv.Notes).FontSize(10).FontColor(Ink);
                        });
                    }
                });
            });
        }).GeneratePdf();   // <= devuelve byte[]

        return bytes;
    }



    // ========= Digital simple (queda por si lo quieres seguir usando) =========
    private async Task<byte[]> RenderDigitalAsync(Invoice inv, Workshop? ws)
    {
        EnsureQuestPdfLicense();
        var brand = await ResolveBrandAsync(ws);

        var bytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(24);
                page.Size(PageSizes.Letter);

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    col.Item().Text(brand.Name).Bold().FontSize(18);
                    if (!string.IsNullOrWhiteSpace(brand.RucLine))
                        col.Item().Text(brand.RucLine);
                    if (!string.IsNullOrWhiteSpace(brand.Description))
                        col.Item().Text(brand.Description).FontSize(10);
                    if (!string.IsNullOrWhiteSpace(brand.ContactLine))
                        col.Item().Text(brand.ContactLine).FontSize(10);
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

                    if (!string.IsNullOrWhiteSpace(inv.Notes))
                    {
                        col.Item().Text("NOTAS").Bold();
                        col.Item().Text(inv.Notes);
                    }
                });
            });
        }).GeneratePdf();

        return bytes;
    }

    // ========= (Opcional) con PNG de fondo =========
    private async Task<byte[]> RenderPreprintedAsync(Invoice inv, Workshop? ws)
    {
        EnsureQuestPdfLicense();
        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var templatePath = Path.Combine(webRoot, "templates", "diogenes_form.png");

        if (!System.IO.File.Exists(templatePath))
            return await RenderStyledAsync(inv, ws); // si no hay imagen, usa el pintado

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
            return await RenderStyledAsync(inv, ws);
        }
    }

}
