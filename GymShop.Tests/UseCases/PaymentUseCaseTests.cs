using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Payments;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public class PaymentUseCaseTests
{
    [Fact]
    public async Task CreatePayment_creates_pending_payment_for_order_total()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);

        var useCase = new CreatePaymentUseCase(db, [new MockPaymentGateway()]);
        var result = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("Mock", null));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(order.Id, result.Value.OrderId);
        Assert.Equal(200, result.Value.Amount);
        Assert.Equal(PaymentStatus.Pending.ToString(), result.Value.Status);
        Assert.Equal("mock-pref-" + order.Id, result.Value.ProviderPreferenceId);
        Assert.StartsWith("server-", result.Value.IdempotencyKey);
        Assert.Equal(result.Value.IdempotencyKey, db.Payments.Single().IdempotencyKey);
        Assert.Single(db.Payments);
    }

    [Fact]
    public async Task CreatePayment_rejects_missing_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var gateway = new FakeMercadoPagoGateway();

        var useCase = new CreatePaymentUseCase(db, [gateway]);
        var result = await useCase.ExecuteAsync(999999, user.Id, false, new CreatePaymentRequest("MercadoPago", "missing-order"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, result.Error?.Type);
        Assert.Equal(0, gateway.CreatePreferenceCalls);
        Assert.Empty(db.Payments);
    }

    [Fact]
    public async Task CreatePayment_rejects_paid_order_without_creating_second_preference()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway();

        var useCase = new CreatePaymentUseCase(db, [gateway]);
        var result = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "paid-order"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal(0, gateway.CreatePreferenceCalls);
        Assert.Empty(db.Payments);
    }
    [Fact]
    public async Task CreateMercadoPagoPayment_uses_gateway_and_persists_preference_data()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var gateway = new FakeMercadoPagoGateway();

        var useCase = new CreatePaymentUseCase(db, [gateway]);
        var result = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "idem-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, gateway.CreatePreferenceCalls);
        Assert.Equal("MercadoPago", result.Value?.Provider);
        Assert.Equal("pref-123", result.Value?.ProviderPreferenceId);
        Assert.Equal("https://sandbox.mercadopago.test/checkout", result.Value?.CheckoutUrl);
        Assert.Equal("idem-1", db.Payments.Single().IdempotencyKey);
        Assert.Equal($"order-{order.Id}-payment-{result.Value?.Id}", gateway.LastExternalReference);
    }

    [Fact]
    public async Task CreatePayment_returns_existing_payment_for_same_idempotency_key()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var gateway = new FakeMercadoPagoGateway();
        var useCase = new CreatePaymentUseCase(db, [gateway]);

        var first = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "idem-1"));
        var payment = await db.Payments.SingleAsync();
        payment.Status = PaymentStatus.Rejected;
        await db.SaveChangesAsync();

        var second = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "idem-1"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value?.Id, second.Value?.Id);
        Assert.Equal(PaymentStatus.Rejected.ToString(), second.Value?.Status);
        Assert.Equal(1, gateway.CreatePreferenceCalls);
        Assert.Single(db.Payments);
    }

    [Fact]
    public async Task CreatePayment_returns_existing_pending_payment_without_calling_gateway_again()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var gateway = new FakeMercadoPagoGateway();
        var useCase = new CreatePaymentUseCase(db, [gateway]);

        var first = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "idem-1"));
        var second = await useCase.ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "idem-2"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value?.Id, second.Value?.Id);
        Assert.Equal(1, gateway.CreatePreferenceCalls);
        Assert.Single(db.Payments);
    }

    [Fact]
    public async Task Webhook_approved_marks_payment_and_order_as_paid()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        var gateway = new FakeMercadoPagoGateway { PaymentStatus = "approved", Amount = order.Total, ExternalReference = $"order-{order.Id}" };

        var useCase = new HandlePaymentWebhookUseCase(db, [gateway]);
        var result = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Approved.ToString(), result.Value?.Status);
        Assert.Equal(PaymentStatus.Approved, db.Payments.Single().Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(["pref-123"], gateway.ExpiredPreferenceIds);
    }

    [Fact]
    public async Task Webhook_approved_retries_preference_expiration_without_applying_payment_twice()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "approved",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}",
            ExpirationFailuresRemaining = 1
        };
        var useCase = new HandlePaymentWebhookUseCase(db, [gateway]);

        var first = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");
        var second = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.False(first.IsSuccess);
        Assert.Equal(AppErrorType.Unavailable, first.Error?.Type);
        Assert.Equal("payment_preference_invalidation_failed", first.Error?.Code);
        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(["pref-123", "pref-123"], gateway.ExpiredPreferenceIds);
        Assert.Single(db.AuditEntries.Where(x => x.Action == "PaymentPreferenceInvalidationFailed"));
        Assert.Single(db.AuditEntries.Where(x => x.Action == "PaymentResolvedByProvider"));
    }

    [Fact]
    public async Task Webhook_approved_with_incorrect_amount_keeps_payment_and_order_pending()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "approved",
            Amount = order.Total - 1m,
            ExternalReference = $"order-{order.Id}"
        };

        var result = await new HandlePaymentWebhookUseCase(db, [gateway])
            .ExecuteAsync("MercadoPago", "mp-pay-wrong-amount");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Contains("monto o la moneda", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Create_payment_rejects_zero_total_without_calling_gateway()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        order.Total = 0;
        var gateway = new FakeMercadoPagoGateway();

        var result = await new CreatePaymentUseCase(db, [gateway])
            .ExecuteAsync(order.Id, user.Id, false, new CreatePaymentRequest("MercadoPago", "free-order"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Empty(db.Payments);
        Assert.Equal(0, gateway.CreatePreferenceCalls);
    }

    [Fact]
    public async Task Webhook_rejected_cancels_order_and_restores_stock_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        var gateway = new FakeMercadoPagoGateway { PaymentStatus = "rejected", Amount = order.Total, ExternalReference = $"order-{order.Id}" };

        var useCase = new HandlePaymentWebhookUseCase(db, [gateway]);
        var first = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");
        var second = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(5, product.Stock);
        Assert.Empty(gateway.ExpiredPreferenceIds);
    }

    [Fact]
    public async Task Webhook_refunded_before_shipping_updates_payment_and_order_and_restores_stock_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        payment.PaidAt = DateTime.UtcNow;
        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "refunded",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}"
        };
        var useCase = new HandlePaymentWebhookUseCase(db, [gateway]);

        var first = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");
        var second = await useCase.ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(5, product.Stock);
        Assert.Contains("Reembolso total", payment.FailureReason);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal("PaymentRefundedByProvider", audit.Action);
        Assert.Null(audit.ActorUserId);
    }

    [Fact]
    public async Task Webhook_refunded_after_shipping_does_not_restore_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        payment.PaidAt = DateTime.UtcNow;
        order.Status = OrderStatus.Shipped;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "refunded",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}"
        };

        var result = await new HandlePaymentWebhookUseCase(db, [gateway])
            .ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(3, product.Stock);
        Assert.Contains("gestion manual", payment.FailureReason);
        Assert.Equal("PaymentRefundedByProvider", Assert.Single(db.AuditEntries).Action);
    }

    [Theory]
    [InlineData(OrderStatus.Preparing, true)]
    [InlineData(OrderStatus.Delivered, false)]
    public async Task Webhook_refund_handles_new_fulfillment_states(OrderStatus initialStatus, bool restoresStock)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        order.Status = initialStatus;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "refunded",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}"
        };

        var result = await new HandlePaymentWebhookUseCase(db, [gateway]).ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(restoresStock ? 5 : 3, product.Stock);
        if (!restoresStock) Assert.Contains("gestion manual", payment.FailureReason);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal("PaymentRefundedByProvider", audit.Action);
        Assert.Contains($"\"orderStatus\":\"{initialStatus}\"", audit.OldValue);
        Assert.Contains("\"orderStatus\":\"Refunded\"", audit.NewValue);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Canceled)]
    [InlineData(OrderStatus.Refunded)]
    public async Task Webhook_refund_rejects_non_refundable_order_states(OrderStatus initialStatus)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        order.Status = initialStatus;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "refunded",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}"
        };

        var result = await new HandlePaymentWebhookUseCase(db, [gateway]).ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(initialStatus, order.Status);
        Assert.Equal(3, product.Stock);
        Assert.Empty(db.AuditEntries);
    }

    [Fact]
    public async Task Partial_refund_is_recorded_for_manual_handling_without_changing_states_or_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync();
        var gateway = new FakeMercadoPagoGateway
        {
            PaymentStatus = "partially_refunded",
            Amount = order.Total,
            ExternalReference = $"order-{order.Id}"
        };

        var result = await new HandlePaymentWebhookUseCase(db, [gateway])
            .ExecuteAsync("MercadoPago", "mp-pay-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(3, product.Stock);
        Assert.Contains("gestion manual", payment.FailureReason);
        Assert.Equal("PaymentPartialRefundFlagged", Assert.Single(db.AuditEntries).Action);
    }

    [Fact]
    public async Task Admin_cannot_mark_approved_payment_as_refunded()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        payment.Status = PaymentStatus.Approved;
        payment.ProviderPaymentId = "mp-pay-1";
        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync();

        var result = await new UpdatePaymentStatusUseCase(db).ExecuteAsync(
            payment.Id,
            new UpdatePaymentStatusRequest("Refunded", "mp-pay-1", "manual"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Empty(db.AuditEntries);
    }

    [Fact]
    public async Task ApprovePayment_marks_order_as_paid()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total);

        var useCase = new UpdatePaymentStatusUseCase(db, new FakeAuditContext(user.Id, "corr-manual-payment"));
        var result = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Approved", "pay_123", null));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Approved.ToString(), result.Value?.Status);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.NotNull(payment.PaidAt);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal("PaymentResolvedManually", audit.Action);
        Assert.Equal(user.Id, audit.ActorUserId);
        Assert.Equal("corr-manual-payment", audit.CorrelationId);
    }

    [Fact]
    public async Task RejectPayment_cancels_order_and_restores_stock_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);
        var product = await db.Products.SingleAsync();
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total);

        Assert.Equal(3, product.Stock);

        var useCase = new UpdatePaymentStatusUseCase(db);
        var first = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Rejected", "pay_123", "Insufficient funds"));
        var second = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Rejected", "pay_123", "Duplicate"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(PaymentStatus.Rejected, payment.Status);
        Assert.Equal(5, product.Stock);
        Assert.Equal("PaymentResolvedManually", Assert.Single(db.AuditEntries).Action);
    }

    [Fact]
    public async Task AdminStatusUpdate_does_not_allow_paid_without_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);

        var useCase = new UpdateOrderStatusUseCase(db);
        var result = await useCase.ExecuteAsync(order.Id, new UpdateOrderStatusRequest("Paid"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task CreatePayment_rejects_order_owned_by_another_user()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var otherUser = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);

        var useCase = new CreatePaymentUseCase(db, [new MockPaymentGateway()]);
        var result = await useCase.ExecuteAsync(order.Id, otherUser.Id, false, new CreatePaymentRequest("Mock", "other-user"));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Forbidden, result.Error?.Type);
        Assert.Empty(db.Payments);
    }

    [Fact]
    public async Task CreatePayment_allows_admin_to_manage_another_users_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var admin = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 2, price: 100);

        var useCase = new CreatePaymentUseCase(db, [new MockPaymentGateway()]);
        var result = await useCase.ExecuteAsync(order.Id, admin.Id, true, new CreatePaymentRequest("Mock", "admin-order"));

        Assert.True(result.IsSuccess);
        Assert.Equal(order.Id, result.Value?.OrderId);
    }
    private static async Task<User> SeedUserAsync(GymShop.Infrastructure.Data.GymShopDbContext db)
    {
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User
        {
            Email = $"cliente-{Guid.NewGuid():N}@test.com",
            Name = "Cliente Test",
            PasswordHash = new PasswordHasher().Hash("123456"),
            RoleId = role.Id,
            Role = role,
            IsActive = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Bank_transfer_approval_requires_reference_and_is_idempotent()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "BankTransfer");
        var useCase = new UpdatePaymentStatusUseCase(db, new FakeAuditContext(user.Id, "bank-confirmation"));

        var missingReference = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Approved", null, null));
        Assert.False(missingReference.IsSuccess);

        var first = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Approved", null, "Movimiento 7788 acreditado"));
        var duplicate = await useCase.ExecuteAsync(payment.Id, new UpdatePaymentStatusRequest("Approved", null, "Movimiento 7788 acreditado"));

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.Status);
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal("Movimiento 7788 acreditado", audit.Reason);
        Assert.Equal(user.Id, audit.ActorUserId);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Canceled")]
    [InlineData("Expired")]
    public async Task MercadoPago_cannot_be_resolved_manually(string status)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        var payment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");

        var result = await new UpdatePaymentStatusUseCase(db).ExecuteAsync(
            payment.Id, new UpdatePaymentStatusRequest(status, null, "No permitido"));

        Assert.False(result.IsSuccess);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task Admin_cancel_with_pending_MercadoPago_restores_stock_once_and_late_approval_creates_idempotent_incident()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        var mercadoPagoPayment = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        mercadoPagoPayment.ProviderPaymentId = "mp-after-admin-attempt";
        await db.SaveChangesAsync();

        var deniedClient = await new CancelOrderUseCase(db).ExecuteAsync(order.Id, user.Id, false, new CancelOrderRequest("Cliente"));
        Assert.False(deniedClient.IsSuccess);
        var missingReason = await new CancelOrderUseCase(db).ExecuteAsync(order.Id, user.Id, true, new CancelOrderRequest(null));
        Assert.False(missingReason.IsSuccess);

        var cancellationUseCase = new CancelOrderUseCase(db, new FakeAuditContext(user.Id, "admin-cancel"));
        var cancellation = await cancellationUseCase.ExecuteAsync(order.Id, user.Id, true, new CancelOrderRequest("Pedido duplicado confirmado por soporte"));
        var duplicateCancellation = await cancellationUseCase.ExecuteAsync(order.Id, user.Id, true, new CancelOrderRequest("Pedido duplicado confirmado por soporte"));
        Assert.True(cancellation.IsSuccess);
        Assert.True(duplicateCancellation.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(5, db.Products.Single().Stock);
        Assert.Single(db.StockMovements);

        var gateway = new FakeMercadoPagoGateway { PaymentStatus = "approved", Amount = order.Total, ExternalReference = $"order-{order.Id}" };
        var webhook = new HandlePaymentWebhookUseCase(db, [gateway]);
        var mismatched = await webhook.ExecuteAsync("MercadoPago", "different-payment");
        Assert.False(mismatched.IsSuccess);
        Assert.Equal(PaymentStatus.Canceled, mercadoPagoPayment.Status);
        var approval = await webhook.ExecuteAsync("MercadoPago", "mp-after-admin-attempt");
        var duplicateApproval = await webhook.ExecuteAsync("MercadoPago", "mp-after-admin-attempt");

        Assert.True(approval.IsSuccess);
        Assert.True(duplicateApproval.IsSuccess);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(PaymentStatus.Approved, db.Payments.Single().Status);
        Assert.Equal(5, db.Products.Single().Stock);
        Assert.Single(db.StockMovements);
        Assert.Contains("requiere revision", db.Payments.Single().FailureReason);
        Assert.Single(db.AuditEntries.Where(x => x.Action == "PaymentApprovedAfterOrderCancellation"));
    }

    [Fact]
    public async Task Webhook_matches_each_of_two_canceled_MercadoPago_attempts_by_unique_external_reference()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var order = await SeedOrderAsync(db, user.Id, stock: 5, quantity: 1, price: 100);
        var first = await CreatePendingPaymentAsync(db, order.Id, order.Total, "MercadoPago");
        first.ExternalReference = $"order-{order.Id}-payment-{first.Id}";
        await db.SaveChangesAsync();
        var canceled = await new CancelOrderUseCase(db).ExecuteAsync(order.Id, user.Id, true, new CancelOrderRequest("Fraude detectado"));
        Assert.True(canceled.IsSuccess);

        var second = new Payment
        {
            OrderId = order.Id, Provider = "MercadoPago", ExternalReference = "temporary",
            Amount = order.Total, Currency = "ARS", Status = PaymentStatus.Canceled
        };
        db.Payments.Add(second);
        await db.SaveChangesAsync();
        second.ExternalReference = $"order-{order.Id}-payment-{second.Id}";
        await db.SaveChangesAsync();

        var gateway = new FakeMercadoPagoGateway { PaymentStatus = "approved", Amount = order.Total };
        var webhook = new HandlePaymentWebhookUseCase(db, [gateway]);
        gateway.ExternalReference = $"order-{order.Id}-payment-999999";
        var unknown = await webhook.ExecuteAsync("MercadoPago", "unknown-provider-id");
        var duplicateUnknown = await webhook.ExecuteAsync("MercadoPago", "unknown-provider-id");
        Assert.False(unknown.IsSuccess);
        Assert.False(duplicateUnknown.IsSuccess);
        Assert.All(new[] { first, second }, payment => Assert.Equal(PaymentStatus.Canceled, payment.Status));

        gateway.ExternalReference = first.ExternalReference;
        Assert.True((await webhook.ExecuteAsync("MercadoPago", "provider-first")).IsSuccess);
        Assert.Equal(PaymentStatus.Approved, first.Status);
        Assert.Equal(PaymentStatus.Canceled, second.Status);

        gateway.ExternalReference = second.ExternalReference;
        Assert.True((await webhook.ExecuteAsync("MercadoPago", "provider-second")).IsSuccess);
        Assert.True((await webhook.ExecuteAsync("MercadoPago", "provider-second")).IsSuccess);
        Assert.Equal(PaymentStatus.Approved, second.Status);
        var legacyOne = new Payment { OrderId = order.Id, Provider = "MercadoPago", ExternalReference = $"order-{order.Id}", Amount = order.Total, Currency = "ARS", Status = PaymentStatus.Canceled };
        var legacyTwo = new Payment { OrderId = order.Id, Provider = "MercadoPago", ExternalReference = $"order-{order.Id}", Amount = order.Total, Currency = "ARS", Status = PaymentStatus.Canceled };
        db.Payments.AddRange(legacyOne, legacyTwo);
        await db.SaveChangesAsync();
        gateway.ExternalReference = $"order-{order.Id}";
        var ambiguousLegacy = await webhook.ExecuteAsync("MercadoPago", "legacy-provider-id");
        var duplicateAmbiguousLegacy = await webhook.ExecuteAsync("MercadoPago", "legacy-provider-id");
        Assert.False(ambiguousLegacy.IsSuccess);
        Assert.False(duplicateAmbiguousLegacy.IsSuccess);
        Assert.Equal(PaymentStatus.Canceled, legacyOne.Status);
        Assert.Equal(PaymentStatus.Canceled, legacyTwo.Status);
        Assert.Equal(OrderStatus.Canceled, order.Status);
        Assert.Equal(5, db.Products.Single().Stock);
        Assert.Single(db.StockMovements);
        Assert.Equal(2, db.AuditEntries.Count(x => x.Action == "PaymentApprovedAfterOrderCancellation"));
        Assert.Equal(2, db.AuditEntries.Count(x => x.Action == "PaymentWebhookUnmatched"));
    }

    private static async Task<Order> SeedOrderAsync(GymShop.Infrastructure.Data.GymShopDbContext db, int userId, int stock, int quantity, decimal price)
    {
        var product = new Product
        {
            Name = "Mancuerna",
            Description = "Mancuerna 10kg",
            Price = price,
            Stock = stock,
            IsActive = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        product.Stock -= quantity;
        var order = new Order
        {
            UserId = userId,
            ShippingAddress = "Av. Siempre Viva 742",
            Status = OrderStatus.Pending,
            Total = price * quantity,
            User = await db.Users.SingleAsync(x => x.Id == userId)
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPrice = product.Price,
            Quantity = quantity,
            Subtotal = price * quantity,
            Product = product
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return await db.Orders.Include(x => x.User).Include(x => x.Items).ThenInclude(x => x.Product).SingleAsync(x => x.Id == order.Id);
    }

    private static async Task<Payment> CreatePendingPaymentAsync(GymShop.Infrastructure.Data.GymShopDbContext db, int orderId, decimal amount, string provider = "Mock")
    {
        var payment = new Payment
        {
            OrderId = orderId,
            Provider = provider,
            ExternalReference = $"order-{orderId}",
            ProviderPreferenceId = provider == "MercadoPago" ? "pref-123" : $"mock-pref-{orderId}",
            Amount = amount,
            Currency = "ARS",
            Status = PaymentStatus.Pending,
            CheckoutUrl = provider == "MercadoPago" ? "https://sandbox.mercadopago.test/checkout" : $"mock://checkout/orders/{orderId}"
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }

    private sealed class FakeMercadoPagoGateway : IPaymentGateway
    {
        public int CreatePreferenceCalls { get; private set; }
        public string PaymentStatus { get; set; } = "approved";
        public string ExternalReference { get; set; } = "order-1";
        public decimal Amount { get; set; } = 200;
        public string? LastExternalReference { get; private set; }
        public int ExpirationFailuresRemaining { get; set; }
        public List<string> ExpiredPreferenceIds { get; } = [];

        public bool CanHandle(string provider) => string.Equals(provider, "MercadoPago", StringComparison.OrdinalIgnoreCase);

        public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
        {
            CreatePreferenceCalls++;
            LastExternalReference = externalReference;
            return Task.FromResult(new PaymentPreferenceResult("MercadoPago", "pref-123", "https://sandbox.mercadopago.test/checkout"));
        }

        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProviderPaymentResult(providerPaymentId, ExternalReference, PaymentStatus, Amount, "ARS", null));
        }

        public Task ExpirePreferenceAsync(string providerPreferenceId, CancellationToken cancellationToken = default)
        {
            ExpiredPreferenceIds.Add(providerPreferenceId);
            if (ExpirationFailuresRemaining > 0)
            {
                ExpirationFailuresRemaining--;
                throw new PaymentGatewayException("No se pudo invalidar la preferencia de prueba.");
            }

            return Task.CompletedTask;
        }
    }
}
