using GymShop.Application.Abstractions;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Payments;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public sealed class GuestCheckoutUseCaseTests
{
    [Fact]
    public async Task Bank_transfer_creates_pending_order_without_reserving_stock_then_reserves_on_approval()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = await AddProduct(db, stock: 3);
        var useCase = CreateUseCase(db);

        var result = await useCase.ExecuteAsync(Request(product.Id, "BankTransfer"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Order", result.Value!.Kind);
        var order = Assert.Single(db.Orders);
        Assert.Null(order.UserId);
        Assert.Equal("invitada@example.com", order.GuestEmail);
        Assert.False(order.StockReserved);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(3, product.Stock);
        var pendingPayment = Assert.Single(order.Payments);
        Assert.Equal(PaymentStatus.Pending, pendingPayment.Status);
        Assert.Matches("^[0-9]{9}$", pendingPayment.ExternalReference);
        Assert.Equal(pendingPayment.ExternalReference, Assert.Single(result.Value.Order!.Payments).ExternalReference);

        var payment = await db.Payments.SingleAsync();
        var approved = await new UpdatePaymentStatusUseCase(db).ExecuteAsync(
            payment.Id, new UpdatePaymentStatusRequest("Approved", null, "Transferencia acreditada"));

        Assert.True(approved.IsSuccess);
        Assert.Equal(1, product.Stock);
        Assert.True(order.StockReserved);
        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public async Task Approved_guest_transfer_without_stock_is_canceled_for_contact_and_refund()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = await AddProduct(db, stock: 2);
        var result = await CreateUseCase(db).ExecuteAsync(Request(product.Id, "BankTransfer"));
        Assert.True(result.IsSuccess);
        product.Stock = 0;
        await db.SaveChangesAsync();

        var payment = await db.Payments.SingleAsync();
        var approved = await new UpdatePaymentStatusUseCase(db).ExecuteAsync(
            payment.Id, new UpdatePaymentStatusRequest("Approved", null, "Transferencia acreditada"));

        Assert.True(approved.IsSuccess);
        Assert.Equal(PaymentStatus.Approved, payment.Status);
        Assert.Equal(OrderStatus.Canceled, (await db.Orders.SingleAsync()).Status);
        Assert.Contains("devolución", payment.FailureReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, product.Stock);
    }

    [Fact]
    public async Task Mercado_pago_creates_checkout_session_but_no_order_before_approval()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = await AddProduct(db, stock: 3);

        var result = await CreateUseCase(db, new MercadoPagoGateway()).ExecuteAsync(Request(product.Id, "MercadoPago"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Checkout", result.Value!.Kind);
        Assert.Empty(db.Orders);
        var checkout = Assert.Single(db.CheckoutSessions);
        Assert.Null(checkout.UserId);
        Assert.Equal(CheckoutStatus.AwaitingPayment, checkout.Status);
        Assert.Equal("https://mercadopago.test/checkout", Assert.Single(checkout.Payments).CheckoutUrl);
        Assert.Equal(3, product.Stock);
    }

    private static GuestCheckoutUseCase CreateUseCase(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        IPaymentGateway? gateway = null) =>
        new(db,
            new ShippingOptions { HomeDeliveryCost = 500, PickupAddress = "Local" },
            new BankTransferOptions { PendingOrderLifetimeHours = 24 },
            gateway is null ? [new BankTransferPaymentGateway()] : [gateway]);

    private static GuestCheckoutRequest Request(int productId, string provider) => new(
        new GuestCustomerRequest("Ana", "Prueba", "invitada@example.com", "+54 341 555 0101"),
        [new GuestCartItemRequest(productId, 2)],
        "HomeDelivery",
        new ShippingAddressRequest("2000", "Santa Fe", "Rosario", "Córdoba", "1234"),
        500, 2000, provider, $"guest-{provider}-{Guid.NewGuid():N}");

    private static async Task<Product> AddProduct(GymShop.Infrastructure.Data.GymShopDbContext db, int stock)
    {
        var product = new Product { Name = "Mancuerna", Description = "", Price = 1000, Stock = stock, IsActive = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private sealed class MercadoPagoGateway : IPaymentGateway
    {
        public bool CanHandle(string provider) => provider.Equals("MercadoPago", StringComparison.OrdinalIgnoreCase);

        public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentPreferenceResult("MercadoPago", "preference-1", "https://mercadopago.test/checkout"));

        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
