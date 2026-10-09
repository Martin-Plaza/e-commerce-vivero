using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Services;

namespace GymShop.Tests.Infrastructure;

public sealed class FiscalInvoicePdfRendererTests
{
    [Fact]
    public void Render_creates_authorized_homologation_invoice_with_cae_and_qr()
    {
        var document = CreateDocument(3);

        var pdf = new FiscalInvoicePdfRenderer(new StoreTimeZone(null)).Render(document);
        var text = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("FACTURA C", text);
        Assert.Contains("HOMOLOGACION - COMPROBANTE SIN VALIDEZ FISCAL PRODUCTIVA", text);
        Assert.Contains("CAE: 86400947232722", text);
        Assert.Contains("P.V. 00001  Nro. 00000001", text);
        Assert.True(pdf.Length > 20_000);
        WritePreviewWhenRequested(pdf);
    }

    [Fact]
    public void Qr_payload_matches_the_official_arca_version_one_contract()
    {
        var url = ArcaInvoiceQr.BuildUrl(CreateDocument(1), new StoreTimeZone(null).TimeZone);
        var encoded = url[(url.IndexOf("?p=", StringComparison.Ordinal) + 3)..];
        using var json = JsonDocument.Parse(Convert.FromBase64String(encoded));
        var root = json.RootElement;

        Assert.Equal("https://www.arca.gob.ar/fe/qr/", url[..url.IndexOf("?p=", StringComparison.Ordinal)]);
        Assert.Equal(1, root.GetProperty("ver").GetInt32());
        Assert.Equal("2026-10-05", root.GetProperty("fecha").GetString());
        Assert.Equal(23376864979, root.GetProperty("cuit").GetInt64());
        Assert.Equal(1, root.GetProperty("ptoVta").GetInt32());
        Assert.Equal(11, root.GetProperty("tipoCmp").GetInt32());
        Assert.Equal(1, root.GetProperty("nroCmp").GetInt64());
        Assert.Equal(33500m, root.GetProperty("importe").GetDecimal());
        Assert.Equal("PES", root.GetProperty("moneda").GetString());
        Assert.Equal(1m, root.GetProperty("ctz").GetDecimal());
        Assert.Equal("E", root.GetProperty("tipoCodAut").GetString());
        Assert.Equal(86400947232722, root.GetProperty("codAut").GetInt64());
        Assert.False(root.TryGetProperty("tipoDocRec", out _));
    }

    [Fact]
    public void Render_paginates_large_invoices()
    {
        var text = Encoding.ASCII.GetString(new FiscalInvoicePdfRenderer(new StoreTimeZone(null)).Render(CreateDocument(70)));
        var pageCount = Regex.Matches(text, @"/Type /Page /Parent").Count;

        Assert.True(pageCount >= 3);
        Assert.Contains($"/Count {pageCount}", text);
        Assert.Contains($"Pagina {pageCount} de {pageCount}", text);
    }

    [Fact]
    public void Render_credit_note_includes_associated_invoice_and_qr_type_13()
    {
        var invoice = CreateDocument(1);
        var creditNote = CreateDocument(1);
        creditNote.Category = BillingDocumentCategory.CreditNote;
        creditNote.Type = BillingDocumentType.CreditNoteC;
        creditNote.DocumentNumber = 2;
        creditNote.RelatedDocumentId = invoice.Id;
        creditNote.RelatedDocument = invoice;

        var pdf = new FiscalInvoicePdfRenderer(new StoreTimeZone(null)).Render(creditNote);
        var text = Encoding.ASCII.GetString(pdf);
        var url = ArcaInvoiceQr.BuildUrl(creditNote, new StoreTimeZone(null).TimeZone);
        using var json = JsonDocument.Parse(Convert.FromBase64String(url[(url.IndexOf("?p=", StringComparison.Ordinal) + 3)..]));

        Assert.Contains("NOTA DE CREDITO C", text);
        Assert.Contains("Factura asociada: 00001-00000001", text);
        Assert.Equal(13, json.RootElement.GetProperty("tipoCmp").GetInt32());
        WritePreviewWhenRequested(pdf, "GYMSHOP_CREDIT_NOTE_PDF_PREVIEW_PATH");
    }

    private static BillingDocument CreateDocument(int itemCount)
    {
        var document = new BillingDocument
        {
            Id = Guid.Parse("81d53082-a463-4bbc-a4c3-7e6b2d4c8f80"),
            OrderId = 29,
            IdempotencyKey = "arca-homologation-order-29",
            Category = BillingDocumentCategory.Invoice,
            Type = BillingDocumentType.InvoiceC,
            Status = BillingDocumentStatus.Authorized,
            Currency = "ARS",
            IssuerBusinessName = "GymShop Homologacion",
            IssuerCuit = "23-37686497-9",
            IssuerTaxCondition = SellerTaxCondition.None,
            IssuerFiscalAddress = "Catamarca 2730, Rosario, Santa Fe",
            RecipientName = "Cliente de Prueba",
            RecipientTaxCondition = RecipientTaxCondition.ConsumerFinal,
            RecipientEmail = "cliente@example.com",
            RecipientAddress = "Av. Siempre Viva 742, Rosario",
            Subtotal = itemCount * 30000,
            ShippingAmount = 3500,
            NetTaxedAmount = itemCount * 30000 + 3500,
            Total = itemCount * 30000 + 3500,
            PointOfSale = 1,
            DocumentNumber = 1,
            AuthorizationProvider = "ARCA-Homologation",
            Cae = "86400947232722",
            CaeExpiresOn = new DateOnly(2026, 10, 15),
            CreatedAtUtc = new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc),
            AuthorizedAtUtc = new DateTime(2026, 10, 5, 15, 0, 1, DateTimeKind.Utc)
        };
        for (var index = 1; index <= itemCount; index++)
            document.Items.Add(new BillingDocumentItem { Id = index, Description = $"Disco de fundicion profesional 20 kg - unidad {index}", Quantity = 1, UnitPrice = 30000, NetAmount = 30000, TotalAmount = 30000 });
        return document;
    }

    private static void WritePreviewWhenRequested(byte[] pdf, string variable = "GYMSHOP_FISCAL_PDF_PREVIEW_PATH")
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, pdf);
    }
}
