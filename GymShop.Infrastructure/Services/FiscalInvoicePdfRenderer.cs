using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using QRCoder;

namespace GymShop.Infrastructure.Services;

public sealed class FiscalInvoicePdfRenderer(IStoreTimeZone storeTimeZone) : IFiscalInvoicePdfRenderer
{
    public byte[] Render(BillingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var qrUrl = ArcaInvoiceQr.BuildUrl(document, storeTimeZone.TimeZone);
        using var qrData = QRCodeGenerator.GenerateQrCode(qrUrl, QRCodeGenerator.ECCLevel.M);
        return FiscalPdfWriter.Write(new InvoiceLayout(document, storeTimeZone.TimeZone, qrData.ModuleMatrix).Build());
    }

    private static void Validate(BillingDocument document)
    {
        if (document.Category is not (BillingDocumentCategory.Invoice or BillingDocumentCategory.CreditNote) ||
            document.Type is not (BillingDocumentType.InvoiceA or BillingDocumentType.InvoiceB or BillingDocumentType.InvoiceC or
                BillingDocumentType.CreditNoteA or BillingDocumentType.CreditNoteB or BillingDocumentType.CreditNoteC) ||
            document.Status != BillingDocumentStatus.Authorized || document.PointOfSale is null ||
            document.DocumentNumber is null || string.IsNullOrWhiteSpace(document.Cae) || document.CaeExpiresOn is null)
            throw new ArgumentException("The document is not a complete authorized fiscal invoice.", nameof(document));
    }

    private sealed class InvoiceLayout(
        BillingDocument document,
        TimeZoneInfo timeZone,
        IReadOnlyList<BitArray> qrModules)
    {
        private const decimal PageWidth = 595;
        private const decimal PageHeight = 842;
        private const decimal Margin = 42;
        private const decimal ContentWidth = PageWidth - Margin * 2;
        private static readonly CultureInfo AmountCulture = CultureInfo.GetCultureInfo("es-AR");
        private readonly List<StringBuilder> _pages = [];
        private StringBuilder _page = null!;
        private decimal _y;

        public IReadOnlyList<string> Build()
        {
            AddPage(false);
            DrawParties();
            DrawItemsHeader();
            var index = 0;
            foreach (var item in document.Items.OrderBy(x => x.Id))
            {
                var lines = Wrap(item.Description, 43);
                var height = Math.Max(26, 12 + lines.Count * 13);
                if (_y - height < 240)
                {
                    AddPage(true);
                    DrawItemsHeader();
                }
                DrawItem(item, lines, height, index++ % 2 == 1);
            }
            if (_y < 310) AddPage(true);
            DrawTotals();
            DrawAuthorization();
            AddFooters();
            return _pages.Select(page => page.ToString()).ToList();
        }

        private void AddPage(bool continuation)
        {
            _page = new StringBuilder();
            _pages.Add(_page);
            Fill(0.07m, 0.09m, 0.07m);
            Rectangle(0, PageHeight, PageWidth, 104, true);
            Text(Margin, 810, 21, true, "GYMSHOP", 1, 1, 1);
            Text(Margin, 786, 10, false, Safe(document.IssuerBusinessName, "Emisor"), 0.8m, 0.86m, 0.79m);
            Text(385, 813, 17, true, continuation ? "CONTINUACION" : InvoiceName(document.Type).ToUpperInvariant(), 1, 1, 1);
            Text(385, 788, 9, false, $"P.V. {document.PointOfSale:00000}  Nro. {document.DocumentNumber:00000000}", 0.8m, 0.86m, 0.79m);
            Fill(0.84m, 0.97m, 0.15m);
            Rectangle(272, 824, 50, 50, true);
            Text(288, 789, 24, true, InvoiceLetter(document.Type), 0.05m, 0.07m, 0.05m);
            Fill(0.28m, 0.08m, 0.08m);
            Rectangle(0, 738, PageWidth, 27, true);
            Text(Margin, 720, 10, true, "HOMOLOGACION - COMPROBANTE SIN VALIDEZ FISCAL PRODUCTIVA", 1, 1, 1);
            _y = 686;
        }

        private void DrawParties()
        {
            var issuedAt = document.CreatedAtUtc;
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc), timeZone);
            var half = (ContentWidth - 12) / 2;
            Stroke(0.76m, 0.78m, 0.75m);
            Rectangle(Margin, _y, half, 130, false);
            Rectangle(Margin + half + 12, _y, half, 130, false);

            Text(Margin + 12, _y - 20, 9, true, "EMISOR", 0.25m, 0.28m, 0.25m);
            DrawWrapped(Margin + 12, _y - 40, half - 24, Safe(document.IssuerBusinessName, "Sin informar"), 10, true);
            DrawWrapped(Margin + 12, _y - 64, half - 24, $"CUIT: {document.IssuerCuit}", 8, false, 0.34m);
            DrawWrapped(Margin + 12, _y - 80, half - 24, SellerCondition(document.IssuerTaxCondition), 8, false, 0.34m);
            DrawWrapped(Margin + 12, _y - 96, half - 24, document.IssuerFiscalAddress, 8, false, 0.34m);
            if (!string.IsNullOrWhiteSpace(document.IssuerGrossIncomeNumber))
                Text(Margin + 12, _y - 118, 7, false, $"IIBB: {document.IssuerGrossIncomeNumber}", 0.4m, 0.42m, 0.4m);

            var right = Margin + half + 24;
            Text(right, _y - 20, 9, true, "RECEPTOR", 0.25m, 0.28m, 0.25m);
            DrawWrapped(right, _y - 40, half - 24, Safe(document.RecipientName, "Consumidor final"), 10, true);
            Text(right, _y - 64, 8, false, RecipientCondition(document.RecipientTaxCondition), 0.34m, 0.34m, 0.34m);
            if (!string.IsNullOrWhiteSpace(document.RecipientDocumentNumber))
                Text(right, _y - 80, 8, false, $"{document.RecipientDocumentType}: {document.RecipientDocumentNumber}", 0.34m, 0.34m, 0.34m);
            DrawWrapped(right, _y - 96, half - 24, document.RecipientAddress, 8, false, 0.34m);
            Text(right, _y - 118, 7, false, $"Emision: {localDate:dd/MM/yyyy}  |  Moneda: {document.Currency}", 0.4m, 0.42m, 0.4m);
            _y -= 154;
        }

        private void DrawItemsHeader()
        {
            Text(Margin, _y, 11, true, "DETALLE", 0.15m, 0.18m, 0.15m);
            _y -= 13;
            Fill(0.16m, 0.19m, 0.16m);
            Rectangle(Margin, _y, ContentWidth, 25, true);
            Text(Margin + 9, _y - 17, 8, true, "DESCRIPCION", 1, 1, 1);
            Text(366, _y - 17, 8, true, "CANT.", 1, 1, 1);
            Text(420, _y - 17, 8, true, "UNITARIO", 1, 1, 1);
            Text(505, _y - 17, 8, true, "TOTAL", 1, 1, 1);
            _y -= 25;
        }

        private void DrawItem(BillingDocumentItem item, IReadOnlyList<string> lines, decimal height, bool shaded)
        {
            if (shaded)
            {
                Fill(0.96m, 0.97m, 0.95m);
                Rectangle(Margin, _y, ContentWidth, height, true);
            }
            Stroke(0.86m, 0.87m, 0.85m);
            Line(Margin, _y - height, Margin + ContentWidth, _y - height);
            var textY = _y - 17;
            foreach (var line in lines)
            {
                Text(Margin + 9, textY, 9, false, line);
                textY -= 13;
            }
            Text(378, _y - 17, 9, false, item.Quantity.ToString(CultureInfo.InvariantCulture));
            Text(420, _y - 17, 9, false, Money(item.UnitPrice));
            Text(500, _y - 17, 9, true, Money(item.TotalAmount));
            _y -= height;
        }

        private void DrawTotals()
        {
            _y -= 18;
            var labelX = 354m;
            var valueX = 490m;
            AmountLine(labelX, valueX, "Subtotal", document.Subtotal);
            if (document.DiscountAmount > 0) AmountLine(labelX, valueX, "Descuento", -document.DiscountAmount);
            AmountLine(labelX, valueX, "Envio", document.ShippingAmount);
            _y -= 4;
            Stroke(0.2m, 0.23m, 0.2m);
            Line(labelX, _y, Margin + ContentWidth, _y);
            _y -= 22;
            Text(labelX, _y, 12, true, "TOTAL");
            Text(valueX - 18, _y, 13, true, $"$ {Money(document.Total)}");
            _y -= 36;
        }

        private void DrawAuthorization()
        {
            var qrSize = 112m;
            DrawQr(Margin, _y, qrSize);
            var detailsX = Margin + qrSize + 20;
            Text(detailsX, _y - 5, 11, true, "AUTORIZADO POR ARCA", 0.13m, 0.17m, 0.13m);
            Text(detailsX, _y - 29, 10, true, $"CAE: {document.Cae}");
            Text(detailsX, _y - 49, 9, false, $"Vencimiento CAE: {document.CaeExpiresOn:dd/MM/yyyy}");
            Text(detailsX, _y - 70, 8, false, $"Comprobante: {document.PointOfSale:00000}-{document.DocumentNumber:00000000}", 0.33m, 0.36m, 0.33m);
            if (document.RelatedDocument?.PointOfSale is not null && document.RelatedDocument.DocumentNumber is not null)
                Text(detailsX, _y - 88, 8, false, $"Factura asociada: {document.RelatedDocument.PointOfSale:00000}-{document.RelatedDocument.DocumentNumber:00000000}", 0.33m, 0.36m, 0.33m);
            DrawWrapped(detailsX, document.RelatedDocument is null ? _y - 91 : _y - 109, ContentWidth - qrSize - 20,
                document.RelatedDocument is null
                    ? "Escanea el codigo para consultar los datos codificados del comprobante."
                    : "Esta nota de credito anula la factura asociada indicada arriba.", 8, false, 0.33m);
            _y -= qrSize + 14;
        }

        private void DrawQr(decimal x, decimal top, decimal size)
        {
            var count = qrModules.Count;
            var module = size / count;
            Fill(1, 1, 1);
            Rectangle(x, top, size, size, true);
            Fill(0, 0, 0);
            for (var row = 0; row < count; row++)
            for (var column = 0; column < count; column++)
                if (qrModules[row][column])
                    Rectangle(x + column * module, top - row * module, module + 0.02m, module + 0.02m, true);
        }

        private void AddFooters()
        {
            for (var index = 0; index < _pages.Count; index++)
            {
                _page = _pages[index];
                Stroke(0.82m, 0.84m, 0.81m);
                Line(Margin, 52, Margin + ContentWidth, 52);
                Text(Margin, 34, 7, false, $"Pedido #{document.OrderId}  |  Documento {document.Id:N}", 0.45m, 0.48m, 0.45m);
                Text(498, 34, 7, false, $"Pagina {index + 1} de {_pages.Count}", 0.45m, 0.48m, 0.45m);
            }
        }

        private void AmountLine(decimal labelX, decimal valueX, string label, decimal amount)
        {
            Text(labelX, _y, 9, false, label, 0.28m, 0.31m, 0.28m);
            Text(valueX, _y, 9, false, $"$ {Money(amount)}");
            _y -= 18;
        }

        private void DrawWrapped(decimal x, decimal y, decimal width, string? value, decimal size, bool bold, decimal gray = 0)
        {
            var maxChars = Math.Max(10, (int)(width / (size * 0.52m)));
            foreach (var line in Wrap(value ?? string.Empty, maxChars).Take(3))
            {
                Text(x, y, size, bold, line, gray, gray, gray);
                y -= size + 3;
            }
        }

        private static List<string> Wrap(string value, int maxChars)
        {
            var words = value.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return ["-"];
            var result = new List<string>();
            var current = new StringBuilder();
            foreach (var source in words)
            {
                var word = source;
                while (word.Length > maxChars)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    result.Add(word[..maxChars]);
                    word = word[maxChars..];
                }
                if (current.Length > 0 && current.Length + word.Length + 1 > maxChars)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        private static string Safe(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        private static string Money(decimal amount) => amount.ToString("N2", AmountCulture);
        private static string InvoiceName(BillingDocumentType type) => type switch
        {
            BillingDocumentType.InvoiceA => "Factura A",
            BillingDocumentType.InvoiceB => "Factura B",
            BillingDocumentType.InvoiceC => "Factura C",
            BillingDocumentType.CreditNoteA => "Nota de credito A",
            BillingDocumentType.CreditNoteB => "Nota de credito B",
            _ => "Nota de credito C"
        };
        private static string InvoiceLetter(BillingDocumentType type) => type switch
        {
            BillingDocumentType.InvoiceA or BillingDocumentType.CreditNoteA => "A",
            BillingDocumentType.InvoiceB or BillingDocumentType.CreditNoteB => "B",
            _ => "C"
        };
        private static string SellerCondition(SellerTaxCondition value) => value switch { SellerTaxCondition.Monotributo => "Responsable Monotributo", SellerTaxCondition.RegisteredTaxpayer => "IVA Responsable Inscripto", SellerTaxCondition.Exempt => "IVA Exento", _ => "Condicion fiscal no informada" };
        private static string RecipientCondition(RecipientTaxCondition value) => value switch { RecipientTaxCondition.RegisteredTaxpayer => "IVA Responsable Inscripto", RecipientTaxCondition.Monotributo => "Responsable Monotributo", RecipientTaxCondition.Exempt => "IVA Exento", RecipientTaxCondition.NonTaxable => "IVA No Alcanzado", RecipientTaxCondition.ForeignClient => "Cliente del Exterior", _ => "Consumidor Final" };

        private void Text(decimal x, decimal y, decimal size, bool bold, string value, decimal r = 0, decimal g = 0, decimal b = 0) =>
            _page.AppendLine(FormattableString.Invariant($"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {r:0.###} {g:0.###} {b:0.###} rg 1 0 0 1 {x:0.##} {y:0.##} Tm ({FiscalPdfWriter.Encode(value)}) Tj ET"));
        private void Fill(decimal r, decimal g, decimal b) => _page.AppendLine(FormattableString.Invariant($"{r:0.###} {g:0.###} {b:0.###} rg"));
        private void Stroke(decimal r, decimal g, decimal b) => _page.AppendLine(FormattableString.Invariant($"{r:0.###} {g:0.###} {b:0.###} RG"));
        private void Rectangle(decimal x, decimal top, decimal width, decimal height, bool fill) => _page.AppendLine(FormattableString.Invariant($"{x:0.##} {top - height:0.##} {width:0.##} {height:0.##} re {(fill ? "f" : "S")}"));
        private void Line(decimal x1, decimal y1, decimal x2, decimal y2) => _page.AppendLine(FormattableString.Invariant($"{x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S"));
    }
}

public static class ArcaInvoiceQr
{
    private const string VerificationUrl = "https://www.arca.gob.ar/fe/qr/";

    public static string BuildUrl(BillingDocument document, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(document);
        var cuit = Digits(document.IssuerCuit);
        if (cuit.Length != 11 || document.PointOfSale is null || document.DocumentNumber is null ||
            string.IsNullOrWhiteSpace(document.Cae) || document.Cae.Length != 14)
            throw new ArgumentException("The invoice does not contain valid ARCA QR data.", nameof(document));
        var issuedAt = document.CreatedAtUtc;
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc), timeZone);
        var payload = new Dictionary<string, object>
        {
            ["ver"] = 1,
            ["fecha"] = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["cuit"] = long.Parse(cuit, CultureInfo.InvariantCulture),
            ["ptoVta"] = document.PointOfSale.Value,
            ["tipoCmp"] = VoucherCode(document.Type),
            ["nroCmp"] = document.DocumentNumber.Value,
            ["importe"] = document.Total,
            ["moneda"] = document.Currency == "ARS" ? "PES" : document.Currency,
            ["ctz"] = 1m,
            ["tipoCodAut"] = "E",
            ["codAut"] = long.Parse(document.Cae, CultureInfo.InvariantCulture)
        };
        var documentCode = RecipientDocumentCode(document.RecipientDocumentType);
        var recipientNumber = Digits(document.RecipientDocumentNumber);
        if (documentCode is not null && recipientNumber.Length > 0)
        {
            payload["tipoDocRec"] = documentCode.Value;
            payload["nroDocRec"] = long.Parse(recipientNumber, CultureInfo.InvariantCulture);
        }
        var json = JsonSerializer.Serialize(payload);
        return $"{VerificationUrl}?p={Convert.ToBase64String(Encoding.UTF8.GetBytes(json))}";
    }

    private static int VoucherCode(BillingDocumentType type) => type switch
    {
        BillingDocumentType.InvoiceA => 1,
        BillingDocumentType.InvoiceB => 6,
        BillingDocumentType.InvoiceC => 11,
        BillingDocumentType.CreditNoteA => 3,
        BillingDocumentType.CreditNoteB => 8,
        BillingDocumentType.CreditNoteC => 13,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Only fiscal documents can be encoded in the ARCA QR.")
    };

    private static int? RecipientDocumentCode(FiscalIdentityDocumentType type) => type switch
    {
        FiscalIdentityDocumentType.Dni => 96,
        FiscalIdentityDocumentType.Cuit => 80,
        FiscalIdentityDocumentType.Cuil => 86,
        FiscalIdentityDocumentType.Passport => 94,
        FiscalIdentityDocumentType.ForeignId => 91,
        _ => null
    };

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}

internal static class FiscalPdfWriter
{
    public static byte[] Write(IReadOnlyList<string> pageStreams)
    {
        var objectCount = 4 + pageStreams.Count * 2;
        var objects = new byte[objectCount + 1][];
        objects[1] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");
        var kids = string.Join(' ', Enumerable.Range(0, pageStreams.Count).Select(index => $"{5 + index * 2} 0 R"));
        objects[2] = Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pageStreams.Count} >>");
        objects[3] = Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objects[4] = Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        for (var index = 0; index < pageStreams.Count; index++)
        {
            var pageObject = 5 + index * 2;
            var contentObject = pageObject + 1;
            var content = Ascii(pageStreams[index]);
            objects[pageObject] = Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObject} 0 R >>");
            objects[contentObject] = Combine(Ascii($"<< /Length {content.Length} >>\nstream\n"), content, Ascii("\nendstream"));
        }
        using var output = new MemoryStream();
        WriteText(output, "%PDF-1.4\n%GYMSHOP\n");
        var offsets = new long[objectCount + 1];
        for (var number = 1; number <= objectCount; number++)
        {
            offsets[number] = output.Position;
            WriteText(output, $"{number} 0 obj\n");
            output.Write(objects[number]);
            WriteText(output, "\nendobj\n");
        }
        var xref = output.Position;
        WriteText(output, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (var number = 1; number <= objectCount; number++) WriteText(output, $"{offsets[number]:D10} 00000 n \n");
        WriteText(output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    public static string Encode(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var original in value)
        {
            var character = original switch { '\u2013' or '\u2014' => '-', '\u2018' or '\u2019' => '\'', '\u201c' or '\u201d' => '"', _ => original };
            if (character is '\\' or '(' or ')') result.Append('\\').Append(character);
            else if (character is >= ' ' and <= '~') result.Append(character);
            else if (character <= 255) result.Append('\\').Append(Convert.ToString(character, 8).PadLeft(3, '0'));
            else result.Append('?');
        }
        return result.ToString();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static void WriteText(Stream stream, string value) => stream.Write(Ascii(value));
    private static byte[] Combine(params byte[][] chunks)
    {
        var result = new byte[chunks.Sum(chunk => chunk.Length)];
        var offset = 0;
        foreach (var chunk in chunks) { Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length); offset += chunk.Length; }
        return result;
    }
}
