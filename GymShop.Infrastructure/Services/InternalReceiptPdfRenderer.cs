using System.Globalization;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;

namespace GymShop.Infrastructure.Services;

public sealed class InternalReceiptPdfRenderer(IStoreTimeZone storeTimeZone) : IReceiptPdfRenderer
{
    private static readonly CultureInfo AmountCulture = CultureInfo.GetCultureInfo("es-AR");

    public byte[] Render(BillingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layout = new ReceiptLayout(document, storeTimeZone.TimeZone);
        return SimplePdfWriter.Write(layout.Build());
    }

    private sealed class ReceiptLayout
    {
        private const decimal PageWidth = 595;
        private const decimal PageHeight = 842;
        private const decimal Margin = 46;
        private const decimal ContentWidth = PageWidth - Margin * 2;
        private readonly BillingDocument _document;
        private readonly TimeZoneInfo _timeZone;
        private readonly List<StringBuilder> _pages = [];
        private StringBuilder _page = null!;
        private decimal _y;

        public ReceiptLayout(BillingDocument document, TimeZoneInfo timeZone)
        {
            _document = document;
            _timeZone = timeZone;
        }

        public IReadOnlyList<string> Build()
        {
            AddPage(false);
            DrawDocumentHeader();
            DrawParties();
            DrawItemsHeader();

            var index = 0;
            foreach (var item in _document.Items.OrderBy(x => x.Id))
            {
                var descriptionLines = Wrap(item.Description, 42);
                var rowHeight = Math.Max(26, 12 + descriptionLines.Count * 13);
                if (_y - rowHeight < 105)
                {
                    AddPage(true);
                    DrawItemsHeader();
                }
                DrawItem(item, descriptionLines, rowHeight, index++ % 2 == 1);
            }

            if (_y < 218) AddPage(true);
            DrawTotals();
            DrawLegalNotice();
            AddFooters();
            return _pages.Select(x => x.ToString()).ToList();
        }

        private void AddPage(bool continuation)
        {
            _page = new StringBuilder();
            _pages.Add(_page);
            Fill(0.075m, 0.09m, 0.075m);
            Rectangle(0, PageHeight, PageWidth, 86, true);
            Text(Margin, 806, 21, true, "GYMSHOP", 1, 1, 1);
            Text(Margin, 782, 10, false,
                string.IsNullOrWhiteSpace(_document.IssuerBusinessName) ? "Comercio" : _document.IssuerBusinessName,
                0.78m, 0.85m, 0.76m);
            Text(395, 803, 13, true, continuation ? "CONTINUACION" : "COMPROBANTE INTERNO", 1, 1, 1);
            Text(395, 783, 9, false, $"Pedido #{_document.OrderId}", 0.78m, 0.85m, 0.76m);
            _y = 736;
        }

        private void DrawDocumentHeader()
        {
            Fill(0.88m, 0.95m, 0.35m);
            Rectangle(Margin, _y, ContentWidth, 34, true);
            Text(Margin + 12, _y - 22, 11, true, "NO VALIDO COMO FACTURA FISCAL", 0.08m, 0.1m, 0.08m);
            _y -= 52;

            Text(Margin, _y, 18, true, "Comprobante interno de compra");
            _y -= 25;
            var issuedAt = _document.AuthorizedAtUtc ?? _document.CreatedAtUtc;
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc), _timeZone);
            Text(Margin, _y, 9, false, $"Emitido: {localDate:dd/MM/yyyy HH:mm}  |  Moneda: {_document.Currency}", 0.35m, 0.38m, 0.35m);
            _y -= 18;
            Text(Margin, _y, 8, false, $"Identificador: {_document.Id}", 0.45m, 0.48m, 0.45m);
            _y -= 26;
        }

        private void DrawParties()
        {
            var boxTop = _y;
            var half = (ContentWidth - 12) / 2;
            Stroke(0.78m, 0.8m, 0.77m);
            Rectangle(Margin, boxTop, half, 100, false);
            Rectangle(Margin + half + 12, boxTop, half, 100, false);

            Text(Margin + 12, boxTop - 20, 9, true, "EMISOR", 0.25m, 0.28m, 0.25m);
            DrawWrapped(Margin + 12, boxTop - 39, half - 24,
                string.IsNullOrWhiteSpace(_document.IssuerBusinessName) ? "Comercio" : _document.IssuerBusinessName, 10, true);
            var issuerDetails = new List<string>();
            if (!string.IsNullOrWhiteSpace(_document.IssuerCuit)) issuerDetails.Add($"CUIT: {_document.IssuerCuit}");
            if (!string.IsNullOrWhiteSpace(_document.IssuerFiscalAddress)) issuerDetails.Add(_document.IssuerFiscalAddress);
            DrawWrapped(Margin + 12, boxTop - 66, half - 24,
                issuerDetails.Count == 0 ? "Datos comerciales no informados" : string.Join(" | ", issuerDetails), 8, false, 0.35m);

            var rightX = Margin + half + 24;
            Text(rightX, boxTop - 20, 9, true, "CLIENTE", 0.25m, 0.28m, 0.25m);
            DrawWrapped(rightX, boxTop - 39, half - 24, _document.RecipientName, 10, true);
            var recipientDetails = new[] { _document.RecipientEmail, _document.RecipientAddress }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            DrawWrapped(rightX, boxTop - 66, half - 24, string.Join(" | ", recipientDetails), 8, false, 0.35m);
            _y -= 122;
        }

        private void DrawItemsHeader()
        {
            Text(Margin, _y, 11, true, "DETALLE DE LA COMPRA", 0.15m, 0.18m, 0.15m);
            _y -= 13;
            Fill(0.16m, 0.19m, 0.16m);
            Rectangle(Margin, _y, ContentWidth, 25, true);
            Text(Margin + 9, _y - 17, 8, true, "PRODUCTO", 1, 1, 1);
            Text(365, _y - 17, 8, true, "CANT.", 1, 1, 1);
            Text(420, _y - 17, 8, true, "UNITARIO", 1, 1, 1);
            Text(506, _y - 17, 8, true, "TOTAL", 1, 1, 1);
            _y -= 25;
        }

        private void DrawItem(BillingDocumentItem item, IReadOnlyList<string> descriptionLines, decimal height, bool shaded)
        {
            if (shaded)
            {
                Fill(0.96m, 0.97m, 0.95m);
                Rectangle(Margin, _y, ContentWidth, height, true);
            }
            Stroke(0.86m, 0.87m, 0.85m);
            Line(Margin, _y - height, Margin + ContentWidth, _y - height);
            var textY = _y - 17;
            foreach (var line in descriptionLines)
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
            var labelX = 360m;
            var valueX = 493m;
            AmountLine(labelX, valueX, "Subtotal", _document.Subtotal, false);
            if (_document.DiscountAmount > 0) AmountLine(labelX, valueX, "Descuento", -_document.DiscountAmount, false);
            AmountLine(labelX, valueX, "Envio", _document.ShippingAmount, false);
            _y -= 5;
            Stroke(0.2m, 0.23m, 0.2m);
            Line(labelX, _y, Margin + ContentWidth, _y);
            _y -= 22;
            Text(labelX, _y, 12, true, "TOTAL");
            Text(valueX - 15, _y, 13, true, $"$ {Money(_document.Total)}");
            _y -= 35;
        }

        private void AmountLine(decimal labelX, decimal valueX, string label, decimal amount, bool bold)
        {
            Text(labelX, _y, 9, bold, label, 0.28m, 0.31m, 0.28m);
            Text(valueX, _y, 9, bold, $"$ {Money(amount)}");
            _y -= 18;
        }

        private void DrawLegalNotice()
        {
            Fill(0.95m, 0.95m, 0.93m);
            Rectangle(Margin, _y, ContentWidth, 50, true);
            Text(Margin + 12, _y - 18, 8, true, "CONSTANCIA INTERNA", 0.28m, 0.3m, 0.27m);
            DrawWrapped(Margin + 12, _y - 34, ContentWidth - 24,
                "Este documento acredita el registro interno de la compra. No es una factura ni un comprobante fiscal valido ante ARCA.",
                8, false, 0.34m);
            _y -= 60;
        }

        private void AddFooters()
        {
            for (var index = 0; index < _pages.Count; index++)
            {
                _page = _pages[index];
                Stroke(0.82m, 0.84m, 0.81m);
                Line(Margin, 54, Margin + ContentWidth, 54);
                Text(Margin, 36, 7, false, $"Pedido #{_document.OrderId}  |  {_document.Id:N}", 0.45m, 0.48m, 0.45m);
                Text(500, 36, 7, false, $"Pagina {index + 1} de {_pages.Count}", 0.45m, 0.48m, 0.45m);
            }
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
            foreach (var sourceWord in words)
            {
                var word = sourceWord;
                while (word.Length > maxChars)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    result.Add(word[..maxChars]);
                    word = word[maxChars..];
                }
                if (current.Length > 0 && current.Length + 1 + word.Length > maxChars)
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

        private static string Money(decimal amount) => amount.ToString("N2", AmountCulture);

        private void Text(decimal x, decimal y, decimal size, bool bold, string value, decimal r = 0, decimal g = 0, decimal b = 0)
        {
            _page.AppendLine(FormattableString.Invariant($"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {r:0.###} {g:0.###} {b:0.###} rg 1 0 0 1 {x:0.##} {y:0.##} Tm ({PdfText.Encode(value)}) Tj ET"));
        }

        private void Fill(decimal r, decimal g, decimal b) => _page.AppendLine(FormattableString.Invariant($"{r:0.###} {g:0.###} {b:0.###} rg"));
        private void Stroke(decimal r, decimal g, decimal b) => _page.AppendLine(FormattableString.Invariant($"{r:0.###} {g:0.###} {b:0.###} RG"));
        private void Rectangle(decimal x, decimal top, decimal width, decimal height, bool fill) =>
            _page.AppendLine(FormattableString.Invariant($"{x:0.##} {top - height:0.##} {width:0.##} {height:0.##} re {(fill ? "f" : "S")}"));
        private void Line(decimal x1, decimal y1, decimal x2, decimal y2) =>
            _page.AppendLine(FormattableString.Invariant($"{x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S"));
    }

    private static class PdfText
    {
        public static string Encode(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (var original in value)
            {
                var character = original switch
                {
                    '\u2013' or '\u2014' => '-',
                    '\u2018' or '\u2019' => '\'',
                    '\u201c' or '\u201d' => '"',
                    _ => original
                };
                if (character is '\\' or '(' or ')') result.Append('\\').Append(character);
                else if (character is >= ' ' and <= '~') result.Append(character);
                else if (character <= 255) result.Append('\\').Append(Convert.ToString(character, 8).PadLeft(3, '0'));
                else result.Append('?');
            }
            return result.ToString();
        }
    }

    private static class SimplePdfWriter
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
            Write(output, "%PDF-1.4\n%GYMSHOP\n");
            var offsets = new long[objectCount + 1];
            for (var number = 1; number <= objectCount; number++)
            {
                offsets[number] = output.Position;
                Write(output, $"{number} 0 obj\n");
                output.Write(objects[number]);
                Write(output, "\nendobj\n");
            }

            var xref = output.Position;
            Write(output, $"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
            for (var number = 1; number <= objectCount; number++) Write(output, $"{offsets[number]:D10} 00000 n \n");
            Write(output, $"trailer\n<< /Size {objectCount + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }

        private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
        private static void Write(Stream stream, string value) => stream.Write(Ascii(value));
        private static byte[] Combine(params byte[][] chunks)
        {
            var result = new byte[chunks.Sum(x => x.Length)];
            var offset = 0;
            foreach (var chunk in chunks) { Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length); offset += chunk.Length; }
            return result;
        }
    }
}
