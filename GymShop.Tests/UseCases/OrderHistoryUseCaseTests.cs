using GymShop.Application.Common;
using GymShop.Application.UseCases.Orders;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public class OrderHistoryUseCaseTests
{
    [Fact]
    public async Task Missing_order_returns_not_found()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(999);
        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.NotFound, result.Error?.Type);
    }

    [Fact]
    public async Task Existing_order_without_relevant_audit_returns_empty_history()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await SeedOrderAsync(db);
        db.AuditEntries.Add(new AuditEntry { Action = "InternalDiagnostic", EntityType = "Order", EntityId = order.Id.ToString(), CorrelationId = "ignored" });
        await db.SaveChangesAsync();

        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
    }

    [Fact]
    public async Task History_includes_automatic_free_order_confirmation()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await SeedOrderAsync(db);
        order.Total = 0;
        db.AuditEntries.Add(new AuditEntry
        {
            Action = "FreeOrderConfirmed", EntityType = "Order", EntityId = order.Id.ToString(),
            OldValue = "{\"status\":\"Pending\",\"total\":0}",
            NewValue = "{\"status\":\"Paid\",\"total\":0}",
            Reason = "Pedido gratuito confirmado.", CorrelationId = "free-order"
        });
        await db.SaveChangesAsync();

        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(order.Id);

        var entry = Assert.Single(result.Value!);
        Assert.Equal("FreeOrderConfirmed", entry.Action);
        Assert.Equal("Pending", entry.PreviousStatus);
        Assert.Equal("Paid", entry.NewStatus);
        Assert.Equal("Automatic", entry.Source);
    }

    [Fact]
    public async Task History_combines_manual_cancellation_automatic_and_provider_events_chronologically()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await SeedOrderAsync(db);
        var actor = order.User!;
        var payment = new Payment { OrderId = order.Id, Provider = "Mock", ExternalReference = $"order-{order.Id}", Amount = order.Total, Status = PaymentStatus.Refunded };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        var first = DateTime.UtcNow.AddMinutes(-3);
        db.AuditEntries.AddRange(
            new AuditEntry { ActorUserId = actor.Id, Action = "OrderStatusChanged", EntityType = "Order", EntityId = order.Id.ToString(), OldValue = "{\"status\":\"Paid\"}", NewValue = "{\"status\":\"Preparing\"}", CreatedAtUtc = first, CorrelationId = "manual" },
            new AuditEntry { Action = "OrderExpiredAdministratively", EntityType = "Order", EntityId = order.Id.ToString(), OldValue = "{\"status\":\"Pending\"}", NewValue = "{\"status\":\"Canceled\"}", Reason = "Pedido pendiente expirado.", CreatedAtUtc = first.AddMinutes(1), CorrelationId = "system" },
            new AuditEntry { Action = "OrderCanceled", EntityType = "Order", EntityId = order.Id.ToString(), OldValue = "{\"status\":\"Pending\"}", NewValue = "{\"status\":\"Canceled\"}", Reason = "Cancelacion solicitada.", CreatedAtUtc = first.AddMinutes(2), CorrelationId = "cancel" },
            new AuditEntry { Action = "PaymentRefundedByProvider", EntityType = "Payment", EntityId = payment.Id.ToString(), OldValue = "{\"paymentStatus\":\"Approved\",\"orderStatus\":\"Delivered\"}", NewValue = "{\"paymentStatus\":\"Refunded\",\"orderStatus\":\"Refunded\"}", Reason = "Devolucion y stock requieren gestion manual.", CreatedAtUtc = first.AddMinutes(3), CorrelationId = "provider" });
        await db.SaveChangesAsync();

        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(order.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        Assert.Equal(result.Value.OrderBy(x => x.CreatedAtUtc).Select(x => x.Id), result.Value.Select(x => x.Id));
        var manual = result.Value[0];
        Assert.Equal("Manual", manual.Source);
        Assert.Equal("Paid", manual.PreviousStatus);
        Assert.Equal("Preparing", manual.NewStatus);
        Assert.Equal(actor.Email, manual.ActorEmail);
        var automatic = result.Value[1];
        Assert.Equal("Automatic", automatic.Source);
        Assert.Null(automatic.ActorUserId);
        Assert.Equal("Pedido pendiente expirado.", automatic.Reason);
        var cancellation = result.Value[2];
        Assert.Equal("OrderCanceled", cancellation.Action);
        Assert.Equal("Canceled", cancellation.NewStatus);
        var provider = result.Value[3];
        Assert.Equal("Provider", provider.Source);
        Assert.Equal("Delivered", provider.PreviousStatus);
        Assert.Equal("Refunded", provider.NewStatus);
        Assert.Contains("gestion manual", provider.Reason);
    }

    [Fact]
    public async Task History_includes_partial_refund_flag_as_provider_event_with_audited_state_and_reason()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await SeedOrderAsync(db);
        var payment = new Payment { OrderId = order.Id, Provider = "MercadoPago", ExternalReference = $"order-{order.Id}", Amount = order.Total, Status = PaymentStatus.Approved };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        db.AuditEntries.Add(new AuditEntry
        {
            Action = "PaymentPartialRefundFlagged",
            EntityType = "Payment",
            EntityId = payment.Id.ToString(),
            OldValue = "{\"status\":\"Approved\",\"failureReason\":null}",
            NewValue = "{\"status\":\"Approved\",\"failureReason\":\"Reembolso parcial informado por el proveedor; requiere gestion manual.\"}",
            Reason = "Reembolso parcial informado por el proveedor; requiere gestion manual.",
            CreatedAtUtc = DateTime.UtcNow,
            CorrelationId = "provider-partial"
        });
        await db.SaveChangesAsync();

        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(order.Id);

        var partial = Assert.Single(result.Value!);
        Assert.Equal("PaymentPartialRefundFlagged", partial.Action);
        Assert.Equal("Provider", partial.Source);
        Assert.Equal("Approved", partial.PreviousStatus);
        Assert.Equal("Approved", partial.NewStatus);
        Assert.Contains("gestion manual", partial.Reason);
        Assert.Null(partial.ActorUserId);
    }

    [Fact]
    public async Task History_includes_arca_homologation_invoice_events()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var order = await SeedOrderAsync(db);
        db.AuditEntries.AddRange(
            new AuditEntry
            {
                Action = "ArcaHomologationInvoiceRequested", EntityType = "Order", EntityId = order.Id.ToString(),
                Reason = "Factura C de homologacion enviada manualmente a ARCA.", CorrelationId = "arca-request"
            },
            new AuditEntry
            {
                Action = "ArcaHomologationInvoiceAuthorized", EntityType = "Order", EntityId = order.Id.ToString(),
                Reason = "ARCA autorizo la factura C de homologacion.", CorrelationId = "arca-authorized"
            });
        await db.SaveChangesAsync();

        var result = await new GetOrderHistoryUseCase(db).ExecuteAsync(order.Id);

        Assert.Equal(2, result.Value!.Count);
        Assert.Equal("ArcaHomologationInvoiceRequested", result.Value[0].Action);
        Assert.Equal("ArcaHomologationInvoiceAuthorized", result.Value[1].Action);
        Assert.All(result.Value, entry => Assert.Equal("Automatic", entry.Source));
    }

    private static async Task<Order> SeedOrderAsync(GymShop.Infrastructure.Data.GymShopDbContext db)
    {
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = $"history-{Guid.NewGuid():N}@test.com", Name = "Ana", LastName = "Admin", PasswordHash = "hash", RoleId = role.Id, Role = role };
        var order = new Order { User = user, ShippingAddress = "Calle 123", Total = 100, Status = OrderStatus.Paid };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }
}
