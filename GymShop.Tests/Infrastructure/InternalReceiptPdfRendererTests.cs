using System.Text;
using System.Text.RegularExpressions;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Services;

namespace GymShop.Tests.Infrastructure;

public sealed class InternalReceiptPdfRendererTests
{
    [Fact]
    public void Render_creates_a_valid_internal_receipt_with_fiscal_warning()
    {
        var document = CreateDocument(3);

        var pdf = new InternalReceiptPdfRenderer(new StoreTimeZone(null)).Render(document);
        var text = Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("NO VALIDO COMO FACTURA FISCAL", text);
        Assert.Contains("Pedido #42", text);
        Assert.Contains("/Type /Page ", text);
        WritePreviewWhenRequested(pdf);
    }

    [Fact]
    public void Render_paginates_large_orders_and_repeats_page_footers()
    {
        var pdf = new InternalReceiptPdfRenderer(new StoreTimeZone(null)).Render(CreateDocument(70));
        var text = Encoding.ASCII.GetString(pdf);
        var pageCount = Regex.Matches(text, @"/Type /Page /Parent").Count;

        Assert.True(pageCount >= 3);
        Assert.Contains($"/Count {pageCount}", text);
        Assert.Contains($"Pagina 1 de {pageCount}", text);
        Assert.Contains($"Pagina {pageCount} de {pageCount}", text);
    }

    private static BillingDocument CreateDocument(int itemCount)
    {
        var document = new BillingDocument
        {
            Id = Guid.Parse("fd6c39f0-1b67-4b95-9edf-78a37db693cd"),
            OrderId = 42,
            IdempotencyKey = "receipt-order-42",
            Category = BillingDocumentCategory.Receipt,
            Type = BillingDocumentType.PurchaseReceipt,
            Status = BillingDocumentStatus.Authorized,
            Currency = "ARS",
            IssuerBusinessName = "GymShop Rosario",
            IssuerCuit = "20-12345678-9",
            IssuerFiscalAddress = "Catamarca 2730, Rosario, Santa Fe",
            RecipientName = "José Pérez",
            RecipientEmail = "jose@example.com",
            RecipientAddress = "Av. Siempre Viva 742, Córdoba",
            Subtotal = itemCount * 30000,
            DiscountAmount = 5000,
            ShippingAmount = 8500,
            Total = itemCount * 30000 + 3500,
            AuthorizedAtUtc = new DateTime(2026, 10, 2, 18, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 10, 2, 18, 0, 0, DateTimeKind.Utc)
        };
        for (var index = 1; index <= itemCount; index++)
        {
            document.Items.Add(new BillingDocumentItem
            {
                Id = index,
                Description = $"Disco de fundición profesional 20 kg - unidad {index}",
                Quantity = 1,
                UnitPrice = 30000,
                NetAmount = 30000,
                TotalAmount = 30000
            });
        }
        return document;
    }

    private static void WritePreviewWhenRequested(byte[] pdf)
    {
        var path = Environment.GetEnvironmentVariable("GYMSHOP_RECEIPT_PDF_PREVIEW_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, pdf);
    }
}
