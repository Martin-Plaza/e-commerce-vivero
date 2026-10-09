using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.UseCases.Billing;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public sealed class BillingDocumentUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateReceipt_snapshots_paid_order_and_approved_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var useCase = CreateUseCase(db);

        var result = await useCase.ExecuteAsync(order.Id, "receipt-order-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(order.Id, result.Value!.OrderId);
        Assert.Equal(order.Payments.Single().Id, result.Value.PaymentId);
        Assert.Equal("PurchaseReceipt", result.Value.Type);
        Assert.Equal("Authorized", result.Value.Status);
        Assert.Equal(35000, result.Value.Total);
        Assert.Equal("Cliente Prueba", result.Value.RecipientName);
        Assert.Equal("Disco 20 kg (DISCO-20)", Assert.Single(result.Value.Items).Description);
        var persisted = await db.BillingDocuments.Include(x => x.Items).SingleAsync();
        Assert.Equal("Internal", persisted.AuthorizationProvider);
        Assert.Equal(Now.UtcDateTime, persisted.AuthorizedAtUtc);
        Assert.Contains(db.AuditEntries, x => x.Action == "PurchaseReceiptCreated" && x.EntityId == order.Id.ToString());
    }

    [Fact]
    public async Task CreateReceipt_is_idempotent_even_when_retry_uses_a_new_key()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Preparing, 35000, PaymentStatus.Approved);
        var useCase = CreateUseCase(db);

        var first = await useCase.ExecuteAsync(order.Id, "receipt-order-2");
        var sameKey = await useCase.ExecuteAsync(order.Id, "receipt-order-2");
        var otherKey = await useCase.ExecuteAsync(order.Id, "receipt-order-2-retry");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value!.Id, sameKey.Value!.Id);
        Assert.Equal(first.Value.Id, otherKey.Value!.Id);
        Assert.Equal(1, await db.BillingDocuments.CountAsync());
        Assert.Equal(1, db.AuditEntries.Count(x => x.Action == "PurchaseReceiptCreated"));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Canceled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task CreateReceipt_rejects_orders_that_are_not_in_paid_lifecycle(OrderStatus status)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, status, 35000, PaymentStatus.Approved);

        var result = await CreateUseCase(db).ExecuteAsync(order.Id, $"receipt-{status}");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error!.Type);
        Assert.Empty(db.BillingDocuments);
    }

    [Fact]
    public async Task CreateReceipt_rejects_non_free_order_without_approved_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Pending);

        var result = await CreateUseCase(db).ExecuteAsync(order.Id, "receipt-without-payment");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error!.Type);
        Assert.Contains("pago aprobado", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateReceipt_allows_a_paid_free_order_without_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 0, null);

        var result = await CreateUseCase(db).ExecuteAsync(order.Id, "receipt-free-order");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PaymentId);
        Assert.Equal(0, result.Value.Total);
    }

    [Fact]
    public async Task CreateReceipt_blocks_electronic_mode_until_Arca_is_integrated()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var profile = new TestBillingProfile { Mode = BillingMode.ElectronicInvoice, ArcaEnabled = true };

        var result = await CreateUseCase(db, profile).ExecuteAsync(order.Id, "fiscal-attempt");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error!.Type);
        Assert.Equal("electronic_invoicing_not_implemented", result.Error.Code);
        Assert.Empty(db.BillingDocuments);
    }

    [Fact]
    public async Task ListDocuments_returns_not_found_for_unknown_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var result = await new GetOrderBillingDocumentsUseCase(db).ExecuteAsync(999);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Customer_documents_returns_only_authorized_documents_for_owned_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var receipt = await CreateUseCase(db).ExecuteAsync(order.Id, "customer-receipt");
        db.BillingDocuments.Add(new BillingDocument
        {
            OrderId = order.Id, IdempotencyKey = "pending-customer-invoice", Category = BillingDocumentCategory.Invoice,
            Type = BillingDocumentType.InvoiceC, Status = BillingDocumentStatus.PendingAuthorization, Currency = "ARS",
            IssuerBusinessName = "GymShop", RecipientName = "Cliente Prueba", Total = 35000, CreatedAtUtc = Now.UtcDateTime
        });
        await db.SaveChangesAsync();

        var result = await new GetCustomerOrderBillingDocumentsUseCase(db).ExecuteAsync(order.Id, order.UserId!.Value);

        Assert.True(result.IsSuccess);
        Assert.Equal(receipt.Value!.Id, Assert.Single(result.Value!).Id);
    }

    [Fact]
    public async Task Customer_documents_and_pdf_hide_another_users_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var receipt = await CreateUseCase(db).ExecuteAsync(order.Id, "private-customer-receipt");
        var otherUserId = order.UserId!.Value + 999;
        var list = await new GetCustomerOrderBillingDocumentsUseCase(db).ExecuteAsync(order.Id, otherUserId);
        var adminPdf = new GetBillingDocumentPdfUseCase(db, new TestReceiptPdfRenderer(), new TestFiscalInvoicePdfRenderer());
        var pdf = await new GetCustomerBillingDocumentPdfUseCase(db, adminPdf)
            .ExecuteAsync(order.Id, receipt.Value!.Id, otherUserId);

        Assert.False(list.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, list.Error!.Type);
        Assert.False(pdf.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, pdf.Error!.Type);
    }

    [Fact]
    public async Task GetPdf_returns_only_an_authorized_receipt_belonging_to_the_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var created = await CreateUseCase(db).ExecuteAsync(order.Id, "receipt-pdf-order");
        var renderer = new TestReceiptPdfRenderer();
        var useCase = new GetBillingDocumentPdfUseCase(db, renderer, new TestFiscalInvoicePdfRenderer());

        var result = await useCase.ExecuteAsync(order.Id, created.Value!.Id);
        var wrongOrder = await useCase.ExecuteAsync(order.Id + 1, created.Value.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("application/pdf", result.Value!.ContentType);
        Assert.Equal([1, 2, 3], result.Value.Content);
        Assert.Contains($"pedido-{order.Id}-comprobante-", result.Value.FileName);
        Assert.Equal(1, renderer.Calls);
        Assert.False(wrongOrder.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, wrongOrder.Error!.Type);
    }

    [Fact]
    public async Task GetPdf_renders_an_authorized_fiscal_invoice()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var invoice = new BillingDocument
        {
            OrderId = order.Id,
            IdempotencyKey = "invoice-pdf-order",
            Category = BillingDocumentCategory.Invoice,
            Type = BillingDocumentType.InvoiceC,
            Status = BillingDocumentStatus.Authorized,
            Currency = "ARS",
            IssuerBusinessName = "GymShop Homologacion",
            IssuerCuit = "23-37686497-9",
            RecipientName = "Cliente Prueba",
            Subtotal = 35000,
            NetTaxedAmount = 35000,
            Total = 35000,
            PointOfSale = 1,
            DocumentNumber = 2,
            Cae = "86400947232722",
            CaeExpiresOn = new DateOnly(2026, 10, 15),
            AuthorizedAtUtc = Now.UtcDateTime
        };
        db.BillingDocuments.Add(invoice);
        await db.SaveChangesAsync();
        var fiscalRenderer = new TestFiscalInvoicePdfRenderer();
        var useCase = new GetBillingDocumentPdfUseCase(db, new TestReceiptPdfRenderer(), fiscalRenderer);

        var result = await useCase.ExecuteAsync(order.Id, invoice.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([4, 5, 6], result.Value!.Content);
        Assert.Equal($"pedido-{order.Id}-factura-00001-00000002.pdf", result.Value.FileName);
        Assert.Equal(1, fiscalRenderer.Calls);
    }

    [Fact]
    public async Task GetPdf_renders_an_authorized_credit_note_with_its_fiscal_filename()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Refunded, 35000, PaymentStatus.Refunded);
        var invoice = new BillingDocument
        {
            OrderId = order.Id, IdempotencyKey = "invoice-for-credit-note-pdf", Category = BillingDocumentCategory.Invoice,
            Type = BillingDocumentType.InvoiceC, Status = BillingDocumentStatus.Authorized, Currency = "ARS",
            IssuerBusinessName = "GymShop Homologacion", IssuerCuit = "23-37686497-9", RecipientName = "Cliente Prueba",
            Subtotal = 35000, NetTaxedAmount = 35000, Total = 35000, PointOfSale = 1, DocumentNumber = 4,
            Cae = "86400947232722", CaeExpiresOn = new DateOnly(2026, 10, 15), AuthorizedAtUtc = Now.UtcDateTime
        };
        var creditNote = new BillingDocument
        {
            OrderId = order.Id, RelatedDocument = invoice, IdempotencyKey = "credit-note-pdf", Category = BillingDocumentCategory.CreditNote,
            Type = BillingDocumentType.CreditNoteC, Status = BillingDocumentStatus.Authorized, Currency = "ARS",
            IssuerBusinessName = "GymShop Homologacion", IssuerCuit = "23-37686497-9", RecipientName = "Cliente Prueba",
            Subtotal = 35000, NetTaxedAmount = 35000, Total = 35000, PointOfSale = 1, DocumentNumber = 2,
            Cae = "86400947232723", CaeExpiresOn = new DateOnly(2026, 10, 15), AuthorizedAtUtc = Now.UtcDateTime
        };
        db.BillingDocuments.AddRange(invoice, creditNote);
        await db.SaveChangesAsync();
        var fiscalRenderer = new TestFiscalInvoicePdfRenderer();

        var result = await new GetBillingDocumentPdfUseCase(db, new TestReceiptPdfRenderer(), fiscalRenderer)
            .ExecuteAsync(order.Id, creditNote.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal([4, 5, 6], result.Value!.Content);
        Assert.Equal($"pedido-{order.Id}-nota-credito-00001-00000002.pdf", result.Value.FileName);
        Assert.Equal(1, fiscalRenderer.Calls);
    }

    [Fact]
    public async Task CreateHomologationInvoice_persists_authorized_invoice_c_with_cae()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(7, 11, 25),
            Authorization = new ArcaInvoiceAuthorization(true, 7, 11, 25, "74123456789012", new DateOnly(2026, 10, 13), null, null)
        };

        var result = await CreateArcaUseCase(db, gateway).ExecuteAsync(order.Id, "arca-order-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("InvoiceC", result.Value!.Type);
        Assert.Equal("Authorized", result.Value.Status);
        Assert.Equal(7, result.Value.PointOfSale);
        Assert.Equal(25, result.Value.DocumentNumber);
        Assert.Equal("ARCA-Homologation", result.Value.AuthorizationProvider);
        Assert.Equal("74123456789012", result.Value.Cae);
        Assert.Equal(new DateOnly(2026, 10, 13), result.Value.CaeExpiresOn);
        Assert.Equal(1, gateway.SequenceCalls);
        Assert.Equal(1, gateway.AuthorizationCalls);
        Assert.Equal(35000, gateway.LastRequest!.Total);
        var persisted = await db.BillingDocuments.SingleAsync();
        Assert.Equal(BillingDocumentStatus.Authorized, persisted.Status);
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationInvoiceRequested");
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationInvoiceAuthorized");
    }

    [Fact]
    public async Task CreateHomologationInvoice_is_idempotent_after_authorization()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Preparing, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(1, 11, 1),
            Authorization = new ArcaInvoiceAuthorization(true, 1, 11, 1, "74123456789012", new DateOnly(2026, 10, 13), null, null)
        };
        var useCase = CreateArcaUseCase(db, gateway);

        var first = await useCase.ExecuteAsync(order.Id, "arca-order-2");
        var retry = await useCase.ExecuteAsync(order.Id, "arca-order-2-retry");

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value!.Id, retry.Value!.Id);
        Assert.Equal(1, await db.BillingDocuments.CountAsync());
        Assert.Equal(1, gateway.SequenceCalls);
        Assert.Equal(1, gateway.AuthorizationCalls);
    }

    [Fact]
    public async Task CreateHomologationInvoice_persists_rejection_and_retries_same_number()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(3, 11, 9),
            Authorization = new ArcaInvoiceAuthorization(false, 3, 11, 9, null, null, "10016", "Fecha invalida")
        };
        var useCase = CreateArcaUseCase(db, gateway);

        var rejected = await useCase.ExecuteAsync(order.Id, "arca-order-3");
        gateway.Authorization = new ArcaInvoiceAuthorization(true, 3, 11, 9, "74123456789013", new DateOnly(2026, 10, 13), null, null);
        var authorized = await useCase.ExecuteAsync(order.Id, "arca-order-3");

        Assert.Equal("Rejected", rejected.Value!.Status);
        Assert.Equal("10016", rejected.Value.RejectionCode);
        Assert.Equal("Authorized", authorized.Value!.Status);
        Assert.Equal(9, authorized.Value.DocumentNumber);
        Assert.Equal(1, gateway.SequenceCalls);
        Assert.Equal(2, gateway.AuthorizationCalls);
        Assert.Equal(1, await db.BillingDocuments.CountAsync());
    }

    [Fact]
    public async Task CreateHomologationInvoice_keeps_pending_document_when_arca_does_not_respond()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(2, 11, 6),
            AuthorizationException = new HttpRequestException("network unavailable")
        };
        var useCase = CreateArcaUseCase(db, gateway);

        var unavailable = await useCase.ExecuteAsync(order.Id, "arca-order-4");
        var pending = await db.BillingDocuments.SingleAsync();
        var pendingStatus = pending.Status;
        var pendingNumber = pending.DocumentNumber;
        gateway.AuthorizationException = null;
        gateway.QueriedDocument = new ArcaAuthorizedDocument(2, 11, 6, 35000, "74123456789014", new DateOnly(2026, 10, 13));
        var retried = await useCase.ExecuteAsync(order.Id, "arca-order-4");

        Assert.False(unavailable.IsSuccess);
        Assert.Equal(AppErrorType.Unavailable, unavailable.Error!.Type);
        Assert.Equal(BillingDocumentStatus.PendingAuthorization, pendingStatus);
        Assert.Equal(6, pendingNumber);
        Assert.Equal("Authorized", retried.Value!.Status);
        Assert.Equal(6, retried.Value.DocumentNumber);
        Assert.Equal(1, gateway.SequenceCalls);
        Assert.Equal(1, gateway.AuthorizationCalls);
        Assert.Equal(1, gateway.QueryCalls);
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationInvoiceRecovered");
    }

    [Fact]
    public async Task CreateHomologationInvoice_does_not_resend_when_reconciliation_is_unavailable()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(2, 11, 7),
            AuthorizationException = new HttpRequestException("authorization response lost")
        };
        var useCase = CreateArcaUseCase(db, gateway);
        await useCase.ExecuteAsync(order.Id, "arca-order-reconciliation-failure");
        gateway.AuthorizationException = null;
        gateway.QueryException = new HttpRequestException("query unavailable");

        var retry = await useCase.ExecuteAsync(order.Id, "arca-order-reconciliation-failure");

        Assert.False(retry.IsSuccess);
        Assert.Equal("arca_reconciliation_unavailable", retry.Error!.Code);
        Assert.Equal(1, gateway.AuthorizationCalls);
        Assert.Equal(1, gateway.QueryCalls);
        Assert.Equal(BillingDocumentStatus.PendingAuthorization, (await db.BillingDocuments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Canceled)]
    public async Task CreateHomologationInvoice_rejects_unpaid_lifecycle_without_calling_arca(OrderStatus status)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, status, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway();

        var result = await CreateArcaUseCase(db, gateway).ExecuteAsync(order.Id, $"arca-{status}");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error!.Type);
        Assert.Equal(0, gateway.SequenceCalls);
        Assert.Equal(0, gateway.AuthorizationCalls);
    }

    [Fact]
    public async Task CreateHomologationCreditNote_authorizes_full_refund_and_links_original_invoice()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(1, 11, 4),
            Authorization = new ArcaInvoiceAuthorization(true, 1, 11, 4, "74123456789012", new DateOnly(2026, 10, 13), null, null),
            CreditNoteSequence = new ArcaInvoiceSequence(1, 13, 2),
            CreditNoteAuthorization = new ArcaInvoiceAuthorization(true, 1, 13, 2, "74123456789015", new DateOnly(2026, 10, 13), null, null)
        };
        var invoiceResult = await CreateArcaUseCase(db, gateway).ExecuteAsync(order.Id, "arca-invoice-for-refund");
        order.Status = OrderStatus.Refunded;
        order.Payments.Single().Status = PaymentStatus.Refunded;
        await db.SaveChangesAsync();

        var useCase = CreateArcaCreditNoteUseCase(db, gateway);
        var result = await useCase.ExecuteAsync(order.Id, "arca-credit-note-1");
        var retry = await useCase.ExecuteAsync(order.Id, "arca-credit-note-1-retry");

        Assert.True(result.IsSuccess);
        Assert.Equal("CreditNoteC", result.Value!.Type);
        Assert.Equal("Authorized", result.Value.Status);
        Assert.Equal(invoiceResult.Value!.Id, result.Value.RelatedDocumentId);
        Assert.Equal("74123456789015", result.Value.Cae);
        Assert.Equal(result.Value.Id, retry.Value!.Id);
        Assert.Equal(2, await db.BillingDocuments.CountAsync());
        Assert.Equal(1, gateway.CreditNoteSequenceCalls);
        Assert.Equal(1, gateway.CreditNoteAuthorizationCalls);
        Assert.Equal(11, gateway.LastCreditNoteRequest!.AssociatedInvoiceType);
        Assert.Equal(4, gateway.LastCreditNoteRequest.AssociatedDocumentNumber);
        Assert.Equal(35000, gateway.LastCreditNoteRequest.Total);
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationCreditNoteRequested");
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationCreditNoteAuthorized");
    }

    [Fact]
    public async Task CreateHomologationCreditNote_recovers_authorization_without_resending()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, OrderStatus.Paid, 35000, PaymentStatus.Approved);
        var gateway = new TestArcaGateway
        {
            Sequence = new ArcaInvoiceSequence(1, 11, 4),
            Authorization = new ArcaInvoiceAuthorization(true, 1, 11, 4, "74123456789012", new DateOnly(2026, 10, 13), null, null),
            CreditNoteSequence = new ArcaInvoiceSequence(1, 13, 2),
            CreditNoteAuthorizationException = new HttpRequestException("authorization response lost")
        };
        await CreateArcaUseCase(db, gateway).ExecuteAsync(order.Id, "arca-invoice-before-recovery");
        order.Status = OrderStatus.Refunded;
        order.Payments.Single().Status = PaymentStatus.Refunded;
        await db.SaveChangesAsync();
        var useCase = CreateArcaCreditNoteUseCase(db, gateway);

        var unavailable = await useCase.ExecuteAsync(order.Id, "arca-credit-note-recovery");
        gateway.CreditNoteAuthorizationException = null;
        gateway.QueriedDocument = new ArcaAuthorizedDocument(1, 13, 2, 35000, "74123456789015", new DateOnly(2026, 10, 13));
        var recovered = await useCase.ExecuteAsync(order.Id, "arca-credit-note-recovery");

        Assert.False(unavailable.IsSuccess);
        Assert.Equal("Authorized", recovered.Value!.Status);
        Assert.Equal("74123456789015", recovered.Value.Cae);
        Assert.Equal(1, gateway.CreditNoteAuthorizationCalls);
        Assert.Equal(1, gateway.QueryCalls);
        Assert.Contains(db.AuditEntries, x => x.Action == "ArcaHomologationCreditNoteRecovered");
    }

    [Theory]
    [InlineData(OrderStatus.Paid, PaymentStatus.Approved)]
    [InlineData(OrderStatus.Refunded, PaymentStatus.Approved)]
    public async Task CreateHomologationCreditNote_requires_provider_confirmed_full_refund(
        OrderStatus orderStatus,
        PaymentStatus paymentStatus)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await AddOrder(db, orderStatus, 35000, paymentStatus);
        var gateway = new TestArcaGateway();

        var result = await CreateArcaCreditNoteUseCase(db, gateway).ExecuteAsync(order.Id, $"credit-note-{orderStatus}-{paymentStatus}");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error!.Type);
        Assert.Equal(0, gateway.CreditNoteSequenceCalls);
    }

    private static CreateOrderReceiptUseCase CreateUseCase(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        IBillingProfile? profile = null) =>
        new(db, profile ?? new TestBillingProfile(), new FixedTimeProvider(Now));

    private static CreateArcaHomologationInvoiceUseCase CreateArcaUseCase(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        TestArcaGateway gateway) =>
        new(db, new HomologationBillingProfile(), gateway, new FixedTimeProvider(Now), new StoreTimeZone(null));

    private static CreateArcaHomologationCreditNoteUseCase CreateArcaCreditNoteUseCase(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        TestArcaGateway gateway) =>
        new(db, gateway, new FixedTimeProvider(Now), new StoreTimeZone(null));

    private static async Task<Order> AddOrder(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        OrderStatus status,
        decimal total,
        PaymentStatus? paymentStatus)
    {
        var user = new User { RoleId = 1, Name = "Cliente", LastName = "Prueba", Email = "cliente@example.com", PasswordHash = "hash" };
        var product = new Product { Name = "Disco 20 kg", Price = 30000, Stock = 1 };
        var order = new Order
        {
            User = user,
            Status = status,
            ShippingAddress = "Catamarca 2730, Rosario",
            Subtotal = total == 0 ? 0 : 30000,
            ShippingCost = total == 0 ? 0 : 5000,
            Total = total,
            Items =
            {
                new OrderItem { Product = product, ProductName = product.Name, VariantSku = "DISCO-20", UnitPrice = total == 0 ? 0 : 30000, Quantity = 1, Subtotal = total == 0 ? 0 : 30000 }
            }
        };
        if (paymentStatus.HasValue)
            order.Payments.Add(new Payment { Provider = "MercadoPago", ExternalReference = "order-test", Amount = total, Currency = "ARS", Status = paymentStatus.Value, PaidAt = paymentStatus == PaymentStatus.Approved ? Now.UtcDateTime : null });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class TestBillingProfile : IBillingProfile
    {
        public BillingMode Mode { get; init; } = BillingMode.ReceiptOnly;
        public SellerTaxCondition TaxCondition { get; init; } = SellerTaxCondition.None;
        public string BusinessName { get; init; } = "GymShop";
        public string Cuit { get; init; } = string.Empty;
        public string FiscalAddress { get; init; } = "Catamarca 2730, Rosario";
        public string GrossIncomeNumber { get; init; } = string.Empty;
        public DateOnly? ActivityStartDate { get; init; }
        public int? PointOfSale { get; init; }
        public bool ArcaEnabled { get; init; }
        public bool ElectronicInvoicingReady => false;
    }

    private sealed class TestReceiptPdfRenderer : IReceiptPdfRenderer
    {
        public int Calls { get; private set; }
        public byte[] Render(BillingDocument document)
        {
            Calls++;
            return [1, 2, 3];
        }
    }

    private sealed class TestFiscalInvoicePdfRenderer : IFiscalInvoicePdfRenderer
    {
        public int Calls { get; private set; }
        public byte[] Render(BillingDocument document)
        {
            Calls++;
            return [4, 5, 6];
        }
    }

    private sealed class HomologationBillingProfile : IBillingProfile
    {
        public BillingMode Mode => BillingMode.ReceiptOnly;
        public SellerTaxCondition TaxCondition => SellerTaxCondition.None;
        public string BusinessName => "GymShop Homologacion";
        public string Cuit => "23-37686497-9";
        public string FiscalAddress => "Catamarca 2730, Rosario";
        public string GrossIncomeNumber => string.Empty;
        public DateOnly? ActivityStartDate => null;
        public int? PointOfSale => null;
        public bool ArcaEnabled => false;
        public bool ElectronicInvoicingReady => false;
    }

    private sealed class TestArcaGateway : IArcaElectronicInvoiceGateway
    {
        public ArcaInvoiceSequence Sequence { get; set; } = new(1, 11, 1);
        public ArcaInvoiceAuthorization Authorization { get; set; } =
            new(true, 1, 11, 1, "74123456789012", new DateOnly(2026, 10, 13), null, null);
        public ArcaInvoiceSequence CreditNoteSequence { get; set; } = new(1, 13, 1);
        public ArcaInvoiceAuthorization CreditNoteAuthorization { get; set; } =
            new(true, 1, 13, 1, "74123456789013", new DateOnly(2026, 10, 13), null, null);
        public int SequenceCalls { get; private set; }
        public int AuthorizationCalls { get; private set; }
        public int CreditNoteSequenceCalls { get; private set; }
        public int CreditNoteAuthorizationCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public ArcaInvoiceAuthorizationRequest? LastRequest { get; private set; }
        public ArcaCreditNoteAuthorizationRequest? LastCreditNoteRequest { get; private set; }
        public Exception? AuthorizationException { get; set; }
        public Exception? CreditNoteAuthorizationException { get; set; }
        public Exception? QueryException { get; set; }
        public ArcaAuthorizedDocument? QueriedDocument { get; set; }

        public Task<ArcaConnectionStatus> CheckConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArcaConnectionStatus("Homologation", true, true, true, [], null, null, Now.UtcDateTime));

        public Task<ArcaInvoiceSequence> GetNextHomologationInvoiceSequenceAsync(CancellationToken cancellationToken = default)
        {
            SequenceCalls++;
            return Task.FromResult(Sequence);
        }

        public Task<ArcaInvoiceAuthorization> AuthorizeHomologationInvoiceAsync(
            ArcaInvoiceAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            AuthorizationCalls++;
            LastRequest = request;
            if (AuthorizationException is not null) throw AuthorizationException;
            return Task.FromResult(Authorization with
            {
                PointOfSale = request.PointOfSale,
                InvoiceType = request.InvoiceType,
                DocumentNumber = request.DocumentNumber
            });
        }

        public Task<ArcaAuthorizedDocument?> GetAuthorizedHomologationDocumentAsync(
            int pointOfSale,
            int documentType,
            long documentNumber,
            CancellationToken cancellationToken = default)
        {
            QueryCalls++;
            if (QueryException is not null) throw QueryException;
            return Task.FromResult(QueriedDocument);
        }

        public Task<ArcaInvoiceSequence> GetNextHomologationCreditNoteSequenceAsync(CancellationToken cancellationToken = default)
        {
            CreditNoteSequenceCalls++;
            return Task.FromResult(CreditNoteSequence);
        }

        public Task<ArcaInvoiceAuthorization> AuthorizeHomologationCreditNoteAsync(
            ArcaCreditNoteAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            CreditNoteAuthorizationCalls++;
            LastCreditNoteRequest = request;
            if (CreditNoteAuthorizationException is not null) throw CreditNoteAuthorizationException;
            return Task.FromResult(CreditNoteAuthorization with
            {
                PointOfSale = request.PointOfSale,
                InvoiceType = request.CreditNoteType,
                DocumentNumber = request.DocumentNumber
            });
        }
    }
}
