using GymShop.Application.Common;
using GymShop.Application.Abstractions;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.UseCases.Carts;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public class CartUseCaseTests
{
    [Fact]
    public async Task AddCartItem_adds_product_and_accumulates_quantity()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();

        var useCase = new AddCartItemUseCase(db);
        var first = await useCase.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        var second = await useCase.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotNull(second.Value);
        Assert.Single(second.Value.Items);
        Assert.Equal(3, second.Value.Items[0].Quantity);
        Assert.Equal(300, second.Value.Total);
    }

    [Fact]
    public async Task AddCartItem_rejects_when_stock_is_insufficient()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 1, price: 100);
        await db.SaveChangesAsync();

        var useCase = new AddCartItemUseCase(db);
        var result = await useCase.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Empty(db.CartItems);
    }

    [Fact]
    public async Task AddCartItem_rejects_zero_quantity()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();

        var useCase = new AddCartItemUseCase(db);
        var result = await useCase.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 0));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Empty(db.CartItems);
    }

    [Fact]
    public async Task CheckoutCart_creates_session_without_reserving_stock_and_clears_cart()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();

        var addToCart = new AddCartItemUseCase(db);
        await addToCart.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));

        var checkout = new CheckoutCartUseCase(db);
        var result = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Av. Siempre Viva 742", 0));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(200, result.Value.Total);
        Assert.Single(result.Value.Items);
        Assert.Equal(5, product.Stock);
        Assert.Empty(db.Orders);
        Assert.Single(db.CheckoutItems);
        Assert.Empty(db.CartItems);
        Assert.NotNull(db.CheckoutSessions.Single().CartClearedAtUtc);
    }

    [Fact]
    public async Task Pending_checkout_allows_a_new_purchase_without_reusing_previous_cart_items()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var firstProduct = SeedProduct(db, stock: 5, price: 100);
        var secondProduct = SeedProduct(db, stock: 4, price: 250);
        await db.SaveChangesAsync();
        var add = new AddCartItemUseCase(db);
        var checkout = new CheckoutCartUseCase(db);

        await add.ExecuteAsync(user.Id, new AddCartItemRequest(firstProduct.Id, 1));
        var first = await checkout.ExecuteAsync(user.Id,
            new CheckoutCartRequest("HomeDelivery", "Primera dirección 123", 0, 100, 0, "checkout-first"));
        await add.ExecuteAsync(user.Id, new AddCartItemRequest(secondProduct.Id, 1));
        var second = await checkout.ExecuteAsync(user.Id,
            new CheckoutCartRequest("HomeDelivery", "Segunda dirección 456", 0, 250, 0, "checkout-second"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, db.CheckoutSessions.Count());
        Assert.Equal(2, db.CheckoutItems.Count());
        Assert.Empty(db.CartItems);
        Assert.Equal(firstProduct.Id, first.Value!.Items.Single().ProductId);
        Assert.Equal(secondProduct.Id, second.Value!.Items.Single().ProductId);
        Assert.Equal(5, firstProduct.Stock);
        Assert.Equal(4, secondProduct.Stock);
    }

    [Fact]
    public async Task CheckoutCart_preserves_product_price_at_purchase_time()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();

        var addToCart = new AddCartItemUseCase(db);
        await addToCart.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));

        var checkout = new CheckoutCartUseCase(db);
        var result = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Av. Siempre Viva 742", 0));
        product.Price = 999;
        await db.SaveChangesAsync();

        var item = await db.CheckoutItems.SingleAsync();
        Assert.True(result.IsSuccess);
        Assert.Equal(100, item.UnitPrice);
        Assert.Equal(200, item.Subtotal);
    }

    [Fact]
    public async Task CheckoutCart_with_two_products_snapshots_both_without_decrementing_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var firstProduct = SeedProduct(db, stock: 5, price: 100);
        var secondProduct = SeedProduct(db, stock: 8, price: 50);
        await db.SaveChangesAsync();

        var addToCart = new AddCartItemUseCase(db);
        await addToCart.ExecuteAsync(user.Id, new AddCartItemRequest(firstProduct.Id, 2));
        await addToCart.ExecuteAsync(user.Id, new AddCartItemRequest(secondProduct.Id, 3));

        var checkout = new CheckoutCartUseCase(db);
        var result = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Av. Siempre Viva 742", 0));

        Assert.True(result.IsSuccess);
        Assert.Equal(5, firstProduct.Stock);
        Assert.Equal(8, secondProduct.Stock);
        Assert.Equal(350, result.Value?.Total);
        Assert.Equal(2, db.CheckoutItems.Count());
    }

    [Fact]
    public async Task CheckoutCart_rejects_missing_product_without_changing_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        var cart = new Cart { UserId = user.Id };
        cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
        cart.Items.Add(new CartItem { ProductId = 999999, Quantity = 1 });
        db.Carts.Add(cart);
        await db.SaveChangesAsync();

        var checkout = new CheckoutCartUseCase(db);
        var result = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Av. Siempre Viva 742", 0));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Equal(5, product.Stock);
        Assert.Empty(db.Orders);
    }

    [Fact]
    public async Task Legacy_pending_order_does_not_block_new_checkout_session()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();

        var addToCart = new AddCartItemUseCase(db);
        await addToCart.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));

        db.Orders.Add(new Order
        {
            UserId = user.Id,
            ShippingAddress = "Av. Siempre Viva 742",
            Status = OrderStatus.Pending,
            Total = 100
        });
        await db.SaveChangesAsync();

        var checkout = new CheckoutCartUseCase(db);
        var result = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Otra direccion 123", 0));

        Assert.True(result.IsSuccess);
        Assert.Single(db.Orders);
        Assert.Single(db.CheckoutSessions);
    }

    [Fact]
    public async Task Pending_order_does_not_block_cart_mutations()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var firstProduct = SeedProduct(db, stock: 8, price: 100);
        var secondProduct = SeedProduct(db, stock: 6, price: 50);
        await db.SaveChangesAsync();

        var add = new AddCartItemUseCase(db);
        Assert.True((await add.ExecuteAsync(user.Id, new AddCartItemRequest(firstProduct.Id, 1))).IsSuccess);
        db.Orders.Add(new Order
        {
            UserId = user.Id,
            ShippingAddress = "Compra anterior 123",
            Status = OrderStatus.Pending,
            Total = 100
        });
        await db.SaveChangesAsync();

        var added = await add.ExecuteAsync(user.Id, new AddCartItemRequest(secondProduct.Id, 2));
        var updated = await new UpdateCartItemUseCase(db).ExecuteAsync(user.Id, firstProduct.Id, new UpdateCartItemRequest(3));
        var removed = await new RemoveCartItemUseCase(db).ExecuteAsync(user.Id, secondProduct.Id);
        var cleared = await new ClearCartUseCase(db).ExecuteAsync(user.Id);

        Assert.True(added.IsSuccess);
        Assert.True(updated.IsSuccess);
        Assert.True(removed.IsSuccess);
        Assert.True(cleared.IsSuccess);
        Assert.Empty(db.CartItems);
        Assert.Single(db.Orders);
        Assert.Equal(OrderStatus.Pending, db.Orders.Single().Status);
    }

    [Fact]
    public async Task Cart_response_removes_coupon_when_content_falls_below_minimum_purchase()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        var coupon = new Coupon { Code = "MINIMUM", Name = "Minimum", Type = CouponType.FixedAmount, Value = 10, MinimumPurchase = 200, IsActive = true };
        db.Coupons.Add(coupon); await db.SaveChangesAsync();
        var add = new AddCartItemUseCase(db);
        await add.ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));
        var applied = await new GymShop.Application.UseCases.Coupons.ApplyCartCouponUseCase(db).ExecuteAsync(user.Id, new GymShop.Application.DTOs.Coupons.ApplyCouponRequest("minimum"));
        Assert.True(applied.IsSuccess);

        var response = await new UpdateCartItemUseCase(db).ExecuteAsync(user.Id, product.Id, new UpdateCartItemRequest(1));

        Assert.True(response.IsSuccess);
        Assert.Equal(100, response.Value!.Subtotal);
        Assert.Equal(0, response.Value.Discount);
        Assert.Equal(100, response.Value.Total);
        Assert.Null(response.Value.CouponCode);
        Assert.Null((await db.Carts.SingleAsync(x => x.UserId == user.Id)).CouponId);
    }

    [Fact]
    public async Task Checkout_applies_coupon_before_home_delivery_cost_and_snapshots_total()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        var coupon = new Coupon { Code = "SAVE20", Name = "Save", Type = CouponType.FixedAmount, Value = 20, IsActive = true };
        db.Coupons.Add(coupon); await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));
        await new GymShop.Application.UseCases.Coupons.ApplyCartCouponUseCase(db)
            .ExecuteAsync(user.Id, new GymShop.Application.DTOs.Coupons.ApplyCouponRequest("SAVE20"));

        var result = await new CheckoutCartUseCase(db, shippingSettings: new TestShippingSettings(30))
            .ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Calle 123", 30, 200, 20));

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Value!.Subtotal);
        Assert.Equal(20, result.Value.DiscountAmount);
        Assert.Equal(30, result.Value.ShippingCost);
        Assert.Equal(210, result.Value.Total);
        Assert.Equal("HomeDelivery", result.Value.DeliveryMethod);
    }

    [Fact]
    public async Task Checkout_with_full_coupon_and_store_pickup_confirms_free_order_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        var coupon = new Coupon { Code = "FREE100", Name = "Free", Type = CouponType.Percentage, Value = 100, IsActive = true };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 2));
        await new GymShop.Application.UseCases.Coupons.ApplyCartCouponUseCase(db)
            .ExecuteAsync(user.Id, new GymShop.Application.DTOs.Coupons.ApplyCouponRequest("FREE100"));
        var checkout = new CheckoutCartUseCase(db, shippingSettings: new TestShippingSettings(50));

        var first = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("StorePickup", null, 0, 200, 200, "checkout-free-key"));
        var retry = await checkout.ExecuteAsync(user.Id, new CheckoutCartRequest("StorePickup", null, 0, 200, 200, "checkout-free-key"));

        Assert.True(first.IsSuccess);
        Assert.Equal(0, first.Value!.Total);
        Assert.Equal(CheckoutStatus.Completed.ToString(), first.Value.Status);
        Assert.NotNull(first.Value.OrderId);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value.Id, retry.Value!.Id);
        Assert.Single(db.Orders);
        Assert.Empty(db.Payments);
        Assert.Equal(3, product.Stock);
        Assert.Single(db.StockMovements);
        Assert.Equal(CouponRedemptionStatus.Consumed, db.CouponRedemptions.Single().Status);
        Assert.Equal("FreeOrderConfirmed", db.AuditEntries.Single().Action);
    }

    [Fact]
    public async Task Checkout_rejects_reusing_idempotency_key_with_different_request_data()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        var checkout = new CheckoutCartUseCase(db);

        var first = await checkout.ExecuteAsync(user.Id,
            new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 100, 0, "checkout-stable-key"));
        var conflictingRetry = await checkout.ExecuteAsync(user.Id,
            new CheckoutCartRequest("HomeDelivery", "Otra calle 456", 0, 100, 0, "checkout-stable-key"));

        Assert.True(first.IsSuccess);
        Assert.False(conflictingRetry.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, conflictingRetry.Error?.Type);
        Assert.Equal("checkout_idempotency_conflict", conflictingRetry.Error?.Code);
        Assert.Single(db.CheckoutSessions);
        Assert.Empty(db.Orders);
        Assert.Equal(5, product.Stock);
    }

    [Fact]
    public async Task Checkout_with_full_coupon_and_home_delivery_leaves_shipping_to_pay()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 5, price: 100);
        var coupon = new Coupon { Code = "PRODUCTS100", Name = "Products free", Type = CouponType.Percentage, Value = 100, IsActive = true };
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));
        await new GymShop.Application.UseCases.Coupons.ApplyCartCouponUseCase(db)
            .ExecuteAsync(user.Id, new GymShop.Application.DTOs.Coupons.ApplyCouponRequest("PRODUCTS100"));

        var result = await new CheckoutCartUseCase(db, shippingSettings: new TestShippingSettings(30))
            .ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Calle 123", 30, 100, 100));

        Assert.True(result.IsSuccess);
        Assert.Equal(30, result.Value!.Total);
        Assert.Equal(CheckoutStatus.AwaitingPayment.ToString(), result.Value.Status);
        Assert.Empty(db.CouponRedemptions);
        Assert.Equal(5, product.Stock);
        Assert.Empty(db.CartItems);
        Assert.Empty(db.AuditEntries.Where(entry => entry.Action == "FreeOrderConfirmed"));
    }

    [Fact]
    public async Task Checkout_store_pickup_snapshots_current_configuration()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 2, price: 100);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));

        var result = await new CheckoutCartUseCase(db, shippingSettings: new TestShippingSettings(999))
            .ExecuteAsync(user.Id, new CheckoutCartRequest("StorePickup", null, 0, 100, 0));

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value!.ShippingAddress);
        Assert.Equal(0, result.Value.ShippingCost);
        Assert.Equal(100, result.Value.Total);
        Assert.Equal("Test pickup", result.Value.PickupAddress);
        Assert.Equal("Test hours", result.Value.PickupHours);
        Assert.Equal("Test instructions", result.Value.PickupInstructions);

        var saved = await db.CheckoutSessions.SingleAsync();
        Assert.Equal("Test pickup", saved.PickupAddress);
        Assert.Equal("Test hours", saved.PickupHours);
        Assert.Equal("Test instructions", saved.PickupInstructions);
    }

    [Fact]
    public async Task Checkout_store_pickup_rejects_missing_pickup_address()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 2, price: 100);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));

        var settings = new TestShippingSettings(50, PickupAddress: "   ");
        var result = await new CheckoutCartUseCase(db, shippingSettings: settings)
            .ExecuteAsync(user.Id, new CheckoutCartRequest("StorePickup", null, 0, 100, 0));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Equal("pickup_configuration_missing", result.Error?.Code);
        Assert.Empty(db.Orders);
        Assert.Equal(2, product.Stock);
    }

    [Fact]
    public async Task Checkout_rejects_stale_shipping_quote_without_creating_order()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db);
        var product = SeedProduct(db, stock: 2, price: 100);
        await db.SaveChangesAsync();
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new AddCartItemRequest(product.Id, 1));

        var result = await new CheckoutCartUseCase(db, shippingSettings: new TestShippingSettings(50))
            .ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Calle 123", 40, 100, 0));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Conflict, result.Error?.Type);
        Assert.Equal("checkout_pricing_changed", result.Error?.Code);
        Assert.Empty(db.Orders);
        Assert.Equal(2, product.Stock);
    }

    private sealed record TestShippingSettings(
        decimal HomeDeliveryCost,
        string PickupAddress = "Test pickup",
        string PickupInstructions = "Test instructions",
        string PickupHours = "Test hours") : IShippingSettings;

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

    private static Product SeedProduct(GymShop.Infrastructure.Data.GymShopDbContext db, int stock, decimal price)
    {
        var product = new Product
        {
            Name = $"Producto {Guid.NewGuid():N}",
            Description = "Producto test",
            Price = price,
            Stock = stock,
            IsActive = true
        };

        db.Products.Add(product);
        return product;
    }
}
