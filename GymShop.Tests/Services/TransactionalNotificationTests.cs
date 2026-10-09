using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Services;

public sealed class TransactionalNotificationTests
{
    private static readonly DateTimeOffset Now = new(2030, 10, 5, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Order_and_status_changes_are_queued_in_the_same_save()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = CreateOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        order.Status = OrderStatus.Preparing;
        await db.SaveChangesAsync();

        var messages = await db.NotificationOutboxMessages.OrderBy(x => x.CreatedAtUtc).ToListAsync();

        Assert.Contains(messages, x => x.Type == TransactionalNotificationType.OrderCreated && x.OrderId == order.Id);
        Assert.Contains(messages, x => x.Type == TransactionalNotificationType.OrderPreparing && x.OrderId == order.Id);
    }

    [Fact]
    public async Task Payment_refund_shipping_and_billing_events_are_queued_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = CreateOrder();
        order.DeliveryMethod = DeliveryMethod.StorePickup;
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var payment = new Payment { Order = order, Provider = "MercadoPago", ExternalReference = "order-test", Amount = order.Total, Status = PaymentStatus.Pending };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        payment.Status = PaymentStatus.Approved;
        await db.SaveChangesAsync();
        payment.Status = PaymentStatus.Refunded;
        await db.SaveChangesAsync();
        order.Status = OrderStatus.Shipped;
        await db.SaveChangesAsync();
        db.BillingDocuments.Add(new BillingDocument
        {
            OrderId = order.Id, Order = order, IdempotencyKey = "receipt-notification", Category = BillingDocumentCategory.Receipt,
            Type = BillingDocumentType.PurchaseReceipt, Status = BillingDocumentStatus.Authorized, Currency = "ARS",
            IssuerBusinessName = "GymShop", RecipientName = "Cliente Prueba", Total = order.Total,
            AuthorizedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var types = await db.NotificationOutboxMessages.Select(x => x.Type).ToListAsync();

        Assert.Equal(1, types.Count(x => x == TransactionalNotificationType.PaymentApproved));
        Assert.Equal(1, types.Count(x => x == TransactionalNotificationType.PaymentRefunded));
        Assert.Equal(1, types.Count(x => x == TransactionalNotificationType.OrderReadyForPickup));
        Assert.Equal(1, types.Count(x => x == TransactionalNotificationType.BillingDocumentAvailable));
    }

    [Fact]
    public async Task Processor_marks_accepted_email_as_sent()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = CreateOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var sender = new FakeSender(EmailSendResult.Accepted());
        var processor = CreateProcessor(db, sender);

        var count = await processor.ProcessBatchAsync();

        var message = await db.NotificationOutboxMessages.SingleAsync();
        Assert.Equal(1, count);
        Assert.Equal(NotificationDeliveryStatus.Sent, message.Status);
        Assert.Equal(1, message.AttemptCount);
        Assert.NotNull(message.SentAtUtc);
        Assert.Contains($"pedido #{order.Id}", sender.LastMessage!.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://demo.example/ordenes", sender.LastMessage.Html);
    }

    [Fact]
    public async Task Processor_schedules_retry_without_changing_the_order_when_provider_rejects_email()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = CreateOrder();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new FakeSender(EmailSendResult.Failed(EmailSendFailureType.HttpRejected, 503)));

        await processor.ProcessBatchAsync();

        var message = await db.NotificationOutboxMessages.SingleAsync();
        Assert.Equal(NotificationDeliveryStatus.Pending, message.Status);
        Assert.Equal("HttpRejected", message.LastFailureType);
        Assert.True(message.NextAttemptAtUtc > Now.UtcDateTime);
        Assert.Equal(OrderStatus.Pending, (await db.Orders.SingleAsync()).Status);
    }

    [Fact]
    public async Task Payment_approved_email_includes_purchase_receipt_pdf()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = CreateOrder();
        order.Items.Add(new OrderItem { ProductId = 1, ProductName = "Mancuerna", Quantity = 2, UnitPrice = 15000, Subtotal = 30000 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        db.NotificationOutboxMessages.RemoveRange(db.NotificationOutboxMessages);
        await db.SaveChangesAsync();
        var payment = new Payment { Order = order, Provider = "MercadoPago", ExternalReference = "order-receipt", Amount = order.Total, Currency = "ARS", Status = PaymentStatus.Pending };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        payment.Status = PaymentStatus.Approved;
        payment.PaidAt = Now.UtcDateTime;
        await db.SaveChangesAsync();
        var sender = new FakeSender(EmailSendResult.Accepted());

        await CreateProcessor(db, sender).ProcessBatchAsync();

        var attachment = Assert.Single(sender.LastMessage!.Attachments!);
        Assert.Equal($"pedido-{order.Id}-comprobante.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(attachment.Content));
        Assert.Contains("Adjuntamos la constancia interna", sender.LastMessage.Html);
    }

    private static TransactionalNotificationProcessor CreateProcessor(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        ITransactionalEmailSender sender) =>
        new(db, sender,
            new InternalReceiptPdfRenderer(new StoreTimeZone(null)),
            new BillingOptions { BusinessName = "GymShop", Cuit = "", FiscalAddress = "" },
            Options.Create(new EmailOptions
        {
            FromName = "GymShop",
            PublicAppUrl = "https://demo.example"
        }), Options.Create(new BankTransferOptions()), new FixedTimeProvider(Now), NullLogger<TransactionalNotificationProcessor>.Instance);

    private static Order CreateOrder() => new()
    {
        User = new User { RoleId = 1, Name = "Cliente", LastName = "Prueba", Email = "cliente@example.com", PasswordHash = "hash" },
        CheckoutIdempotencyKey = Guid.NewGuid().ToString(),
        Status = OrderStatus.Pending,
        ShippingAddress = "Catamarca 2730, Rosario",
        Subtotal = 30000,
        ShippingCost = 5000,
        Total = 35000
    };

    private sealed class FakeSender(EmailSendResult result) : ITransactionalEmailSender
    {
        public TransactionalEmailMessage? LastMessage { get; private set; }
        public Task<EmailSendResult> SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
