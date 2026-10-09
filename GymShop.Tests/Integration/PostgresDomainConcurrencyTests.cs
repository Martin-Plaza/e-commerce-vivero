using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Products;
using GymShop.Application.DTOs.Stock;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Products;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Postgres")]
[Trait("Category", "Concurrency")]
public sealed class PostgresDomainConcurrencyTests
{
    [Fact]
    public async Task Concurrent_checkouts_do_not_reserve_the_same_last_coupon_use()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var (firstUser, secondUser, _) = await SeedTwoCartsWithCouponAsync(database);
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var outcomes = await Task.WhenAll(
            new CheckoutCartUseCase(firstDb, new EfTransactionManager(firstDb)).ExecuteAsync(firstUser, new CheckoutCartRequest("HomeDelivery", "First Address", 0)),
            new CheckoutCartUseCase(secondDb, new EfTransactionManager(secondDb)).ExecuteAsync(secondUser, new CheckoutCartRequest("HomeDelivery", "Second Address", 0)));
        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
        await using var verification = database.CreateContext();
        Assert.Empty(await verification.CouponRedemptions.ToListAsync());
        Assert.Equal(2, await verification.CheckoutSessions.CountAsync());
    }

    [Fact]
    public async Task Concurrent_checkouts_do_not_reserve_or_decrement_the_same_last_stock()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var (firstUser, secondUser, productId) = await SeedTwoCartsAsync(database, stock: 1);
        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();

        var outcomes = await Task.WhenAll(
            new CheckoutCartUseCase(firstDb, new EfTransactionManager(firstDb))
                .ExecuteAsync(firstUser, new CheckoutCartRequest("HomeDelivery", "First Address", 0)),
            new CheckoutCartUseCase(secondDb, new EfTransactionManager(secondDb))
                .ExecuteAsync(secondUser, new CheckoutCartRequest("HomeDelivery", "Second Address", 0)));

        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
        await using var verification = database.CreateContext();
        Assert.Equal(1, (await verification.Products.SingleAsync(x => x.Id == productId)).Stock);
        Assert.Empty(await verification.Orders.ToListAsync());
        Assert.Equal(2, await verification.CheckoutSessions.CountAsync());
        Assert.Empty(await verification.CartItems.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_checkout_retries_with_same_key_return_the_same_session_once()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        int userId;
        int productId;
        await using (var seed = database.CreateContext())
        {
            var role = await seed.Roles.SingleAsync(x => x.Name == "User");
            var user = new User { Email = $"idem-{Guid.NewGuid():N}@test.com", Name = "Idem", PasswordHash = "x", RoleId = role.Id, IsActive = true };
            var product = new Product { Name = "Idempotent Product", Price = 100, Stock = 2, IsActive = true };
            seed.AddRange(user, product);
            await seed.SaveChangesAsync();
            var cart = new Cart { UserId = user.Id };
            cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
            seed.Carts.Add(cart);
            await seed.SaveChangesAsync();
            userId = user.Id;
            productId = product.Id;
        }

        await using var firstDb = database.CreateContext();
        await using var secondDb = database.CreateContext();
        var request = new CheckoutCartRequest("HomeDelivery", "Calle idempotente 123", 0, 100, 0, "same-checkout-key");
        var outcomes = await Task.WhenAll(
            new CheckoutCartUseCase(firstDb, new EfTransactionManager(firstDb)).ExecuteAsync(userId, request),
            new CheckoutCartUseCase(secondDb, new EfTransactionManager(secondDb)).ExecuteAsync(userId, request));

        Assert.All(outcomes, outcome => Assert.True(outcome.IsSuccess));
        Assert.Single(outcomes.Select(outcome => outcome.Value!.Id).Distinct());
        await using var verification = database.CreateContext();
        Assert.Single(await verification.CheckoutSessions.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await verification.Orders.Where(x => x.UserId == userId).ToListAsync());
        Assert.Equal(2, (await verification.Products.SingleAsync(x => x.Id == productId)).Stock);
        Assert.Empty(await verification.CartItems.Where(x => x.Cart.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_stock_updates_have_one_winner_and_one_conflict()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using var lookup = database.CreateContext();
        var productId = await lookup.OrderItems.Where(x => x.OrderId == seed.OrderId).Select(x => x.ProductId).SingleAsync();
        var initialStock = await lookup.Products.Where(x => x.Id == productId).Select(x => x.Stock).SingleAsync();
        var barrier = new ProductSaveBarrier(2);
        await using var firstDb = database.CreateContext(barrier);
        await using var secondDb = database.CreateContext(barrier);
        var first = new AdjustStockUseCase(firstDb).ExecuteAsync(productId, new ManualStockAdjustmentRequest(7, "Corrección concurrente"));
        var second = new AdjustStockUseCase(secondDb).ExecuteAsync(productId, new ManualStockAdjustmentRequest(17, "Corrección concurrente"));
        await barrier.AllArrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        barrier.Release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x.IsSuccess);
        Assert.Single(results, x => !x.IsSuccess && x.Error?.Type == GymShop.Application.Common.AppErrorType.Conflict);
        await using var verification = database.CreateContext();
        Assert.Contains((await verification.Products.SingleAsync(x => x.Id == productId)).Stock,
            new[] { initialStock + 7, initialStock + 17 });
    }

    private static async Task<(int FirstUser, int SecondUser, int ProductId)> SeedTwoCartsAsync(SqlTestDatabase database, int stock)
    {
        await using var db = database.CreateContext();
        var role = await db.Roles.SingleAsync(x => x.Name == "User");
        var first = new User { Email = $"first-{Guid.NewGuid():N}@test.com", Name = "First", PasswordHash = "x", RoleId = role.Id, IsActive = true };
        var second = new User { Email = $"second-{Guid.NewGuid():N}@test.com", Name = "Second", PasswordHash = "x", RoleId = role.Id, IsActive = true };
        var product = new Product { Name = "Last Stock", Price = 100, Stock = stock, IsActive = true };
        db.AddRange(first, second, product);
        await db.SaveChangesAsync();
        var firstCart = new Cart { UserId = first.Id };
        firstCart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
        var secondCart = new Cart { UserId = second.Id };
        secondCart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
        db.Carts.AddRange(firstCart, secondCart);
        await db.SaveChangesAsync();
        return (first.Id, second.Id, product.Id);
    }

    private static async Task<(int FirstUser, int SecondUser, int CouponId)> SeedTwoCartsWithCouponAsync(SqlTestDatabase database)
    {
        await using var db = database.CreateContext();
        var role = await db.Roles.SingleAsync(x => x.Name == "User");
        var first = new User { Email = $"coupon-first-{Guid.NewGuid():N}@test.com", Name = "First", PasswordHash = "x", RoleId = role.Id, IsActive = true };
        var second = new User { Email = $"coupon-second-{Guid.NewGuid():N}@test.com", Name = "Second", PasswordHash = "x", RoleId = role.Id, IsActive = true };
        var firstProduct = new Product { Name = "First Product", Price = 100, Stock = 2, IsActive = true };
        var secondProduct = new Product { Name = "Second Product", Price = 100, Stock = 2, IsActive = true };
        var coupon = new Coupon { Code = $"LAST-{Guid.NewGuid():N}"[..20], Name = "Last use", Type = CouponType.FixedAmount, Value = 10, TotalUsageLimit = 1, IsActive = true };
        db.AddRange(first, second, firstProduct, secondProduct, coupon); await db.SaveChangesAsync();
        var firstCart = new Cart { UserId = first.Id, CouponId = coupon.Id }; firstCart.Items.Add(new CartItem { ProductId = firstProduct.Id, Quantity = 1 });
        var secondCart = new Cart { UserId = second.Id, CouponId = coupon.Id }; secondCart.Items.Add(new CartItem { ProductId = secondProduct.Id, Quantity = 1 });
        db.Carts.AddRange(firstCart, secondCart); await db.SaveChangesAsync();
        return (first.Id, second.Id, coupon.Id);
    }

    private static async Task<(bool Success, Exception? Error)> Capture<T>(Func<Task<T>> operation)
    {
        try { await operation(); return (true, null); }
        catch (Exception ex) { return (false, ex); }
    }

    private sealed class ProductSaveBarrier(int arrivals) : SaveChangesInterceptor
    {
        private int _arrivals;
        public TaskCompletionSource AllArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var changesProduct = eventData.Context?.ChangeTracker.Entries<Product>()
                .Any(x => x.State == EntityState.Modified) == true;
            if (changesProduct)
            {
                if (Interlocked.Increment(ref _arrivals) == arrivals) AllArrived.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
