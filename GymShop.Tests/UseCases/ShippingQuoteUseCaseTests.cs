using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.UseCases.Carts;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public class ShippingQuoteUseCaseTests
{
    private static readonly ShippingAddressRequest Destination = new(
        "C1414ABC", "CABA", "Villa Crespo", "Av. Corrientes", "5500", "2", "B", "Timbre 3");

    [Fact]
    public async Task Quote_and_checkout_snapshot_server_side_shipping_data()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedCartAsync(db, completeDimensions: true);
        var settings = new QuoteSettings();
        var quoteUseCase = CreateQuoteUseCase(db, settings);

        var quoteResult = await quoteUseCase.ExecuteAsync(user.Id, new CreateShippingQuoteRequest(Destination));
        var quote = Assert.Single(quoteResult.Value!);
        var checkout = await new CheckoutCartUseCase(db, shippingSettings: settings).ExecuteAsync(
            user.Id,
            new CheckoutCartRequest("HomeDelivery", null, quote.Price, 100, 0, "shipping-quote-checkout", quote.Id, Destination));

        Assert.True(checkout.IsSuccess);
        Assert.Equal(6500, checkout.Value!.ShippingCost);
        var session = await db.CheckoutSessions.SingleAsync();
        Assert.Equal(quote.Id, session.ShippingQuoteId);
        Assert.Equal(OwnFleetShippingProvider.ProviderCode, session.ShippingProviderCode);
        Assert.Equal("standard", session.ShippingServiceCode);
        Assert.Equal("C1414ABC", session.ShippingPostalCode);
        Assert.Contains("Av. Corrientes 5500", session.ShippingAddress);
    }

    [Fact]
    public async Task Quote_rejects_products_without_complete_package_dimensions()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedCartAsync(db, completeDimensions: false);

        var result = await CreateQuoteUseCase(db, new QuoteSettings()).ExecuteAsync(user.Id, new CreateShippingQuoteRequest(Destination));

        Assert.False(result.IsSuccess);
        Assert.Equal("shipping_dimensions_missing", result.Error?.Code);
        Assert.Empty(db.ShippingQuoteReservations);
    }

    [Fact]
    public async Task Checkout_rejects_quote_after_cart_changes()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedCartAsync(db, completeDimensions: true);
        var settings = new QuoteSettings();
        var quote = Assert.Single((await CreateQuoteUseCase(db, settings).ExecuteAsync(user.Id, new CreateShippingQuoteRequest(Destination))).Value!);
        var productId = await db.Products.Select(x => x.Id).SingleAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(productId, 1));

        var result = await new CheckoutCartUseCase(db, shippingSettings: settings).ExecuteAsync(
            user.Id, new CheckoutCartRequest("HomeDelivery", null, quote.Price, null, null, "changed-cart", quote.Id, Destination));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal("shipping_quote_cart_changed", result.Error?.Code);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task Checkout_rejects_expired_quote()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedCartAsync(db, completeDimensions: true);
        var settings = new QuoteSettings();
        var quote = Assert.Single((await CreateQuoteUseCase(db, settings).ExecuteAsync(user.Id, new CreateShippingQuoteRequest(Destination))).Value!);
        var reservation = await db.ShippingQuoteReservations.SingleAsync(x => x.Id == quote.Id);
        reservation.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await db.SaveChangesAsync();

        var result = await new CheckoutCartUseCase(db, shippingSettings: settings).ExecuteAsync(
            user.Id, new CheckoutCartRequest("HomeDelivery", null, quote.Price, null, null, "expired-quote", quote.Id, Destination));

        Assert.False(result.IsSuccess);
        Assert.Equal("shipping_quote_expired", result.Error?.Code);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task Checkout_rejects_quote_for_a_different_address()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedCartAsync(db, completeDimensions: true);
        var settings = new QuoteSettings();
        var quote = Assert.Single((await CreateQuoteUseCase(db, settings).ExecuteAsync(user.Id, new CreateShippingQuoteRequest(Destination))).Value!);
        var changedDestination = Destination with { StreetNumber = "5502" };

        var result = await new CheckoutCartUseCase(db, shippingSettings: settings).ExecuteAsync(
            user.Id, new CheckoutCartRequest("HomeDelivery", null, quote.Price, null, null, "changed-address", quote.Id, changedDestination));

        Assert.False(result.IsSuccess);
        Assert.Equal("shipping_quote_address_changed", result.Error?.Code);
        Assert.Empty(db.Orders);
    }

    private static QuoteCartShippingUseCase CreateQuoteUseCase(GymShop.Infrastructure.Data.GymShopDbContext db, QuoteSettings settings) =>
        new(db, [new OwnFleetShippingProvider(settings, TimeProvider.System)], settings, TimeProvider.System);

    private static async Task<User> SeedCartAsync(GymShop.Infrastructure.Data.GymShopDbContext db, bool completeDimensions)
    {
        var role = await db.Roles.SingleAsync(x => x.Name == "User");
        var user = new User
        {
            Email = $"shipping-{Guid.NewGuid():N}@test.com",
            Name = "Shipping",
            PasswordHash = "hash",
            RoleId = role.Id,
            Role = role
        };
        var product = new Product
        {
            Name = "Mancuerna embalada",
            Price = 100,
            Stock = 5,
            IsActive = true,
            PackageWeightGrams = completeDimensions ? 2500 : null,
            PackageLengthCm = completeDimensions ? 30 : null,
            PackageWidthCm = completeDimensions ? 15 : null,
            PackageHeightCm = completeDimensions ? 10 : null
        };
        db.AddRange(user, product);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        return user;
    }

    private sealed class QuoteSettings : IShippingSettings
    {
        public decimal HomeDeliveryCost => 6500;
        public string PickupAddress => "Av. Corrientes 1234";
        public string PickupInstructions => "Retirar con DNI";
        public string PickupHours => "10 a 18";
        public string OriginPostalCode => "C1043AAZ";
        public string OriginProvince => "CABA";
        public string OriginCity => "CABA";
        public string OriginStreet => "Av. Corrientes";
        public string OriginStreetNumber => "1234";
        public int QuoteLifetimeMinutes => 15;
        public int EstimatedDeliveryMinDays => 1;
        public int EstimatedDeliveryMaxDays => 3;
    }
}
