using GymShop.Application.DTOs.Audit;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.UseCases.Audit;
using GymShop.Application.UseCases.Carts;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Postgres")]
public sealed class PostgresBehaviorTests
{
    [Fact]
    public async Task Empty_database_applies_all_migrations_and_translates_audit_query()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        db.AuditEntries.Add(new AuditEntry
        {
            Action = "SqlTranslation", EntityType = "Product", EntityId = "1", CorrelationId = "sql-query"
        });
        await db.SaveChangesAsync();
        var result = await new GetAuditEntriesUseCase(db).ExecuteAsync(
            new AuditQueryRequest(EntityType: "Product", EntityId: "1"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task Postgres_allows_legacy_pending_orders_and_enforces_product_checks_and_lengths()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using (var legacyContext = database.CreateContext())
        {
            legacyContext.Orders.Add(new Order
            {
                UserId = seed.UserId, Total = 10, Status = OrderStatus.Pending, ShippingAddress = "Legacy pending"
            });
            await legacyContext.SaveChangesAsync();
            Assert.Equal(2, await legacyContext.Orders.CountAsync(x => x.UserId == seed.UserId && x.Status == OrderStatus.Pending));
        }
        await using (var checkContext = database.CreateContext())
        {
            checkContext.Products.Add(new Product { Name = "Invalid stock", Price = 10, Stock = -1, IsActive = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => checkContext.SaveChangesAsync());
        }
        await using (var lengthContext = database.CreateContext())
        {
            lengthContext.Products.Add(new Product { Name = new string('N', 151), Price = 10, Stock = 1, IsActive = true });
            await Assert.ThrowsAsync<DbUpdateException>(() => lengthContext.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task RowVersion_detects_concurrent_product_updates()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var productId = await first.OrderItems.Where(x => x.OrderId == seed.OrderId).Select(x => x.ProductId).SingleAsync();
        var firstProduct = await first.Products.SingleAsync(x => x.Id == productId);
        var secondProduct = await second.Products.SingleAsync(x => x.Id == productId);

        firstProduct.Stock = 3;
        await first.SaveChangesAsync();
        secondProduct.Stock = 2;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Checkout_transaction_rolls_back_session_and_preserves_stock_order_and_cart_when_post_save_step_fails()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        int userId;
        int productId;
        await using (var seed = database.CreateContext())
        {
            var role = await seed.Roles.SingleAsync(x => x.Name == "User");
            var user = new User { Email = "rollback@test.com", Name = "Rollback", PasswordHash = "not-used", RoleId = role.Id, IsActive = true };
            var product = new Product { Name = "Rollback Product", Price = 100, Stock = 2, IsActive = true };
            seed.AddRange(user, product);
            await seed.SaveChangesAsync();
            var cart = new Cart { UserId = user.Id };
            cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
            seed.Carts.Add(cart);
            await seed.SaveChangesAsync();
            userId = user.Id;
            productId = product.Id;
        }

        await using (var failing = database.CreateContext(new ThrowAfterSaveInterceptor()))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new CheckoutCartUseCase(failing, new EfTransactionManager(failing))
                    .ExecuteAsync(userId, new CheckoutCartRequest("HomeDelivery", "Rollback Address", 0)));
        }

        await using var verification = database.CreateContext();
        Assert.Equal(2, (await verification.Products.SingleAsync(x => x.Id == productId)).Stock);
        Assert.Empty(await verification.CheckoutSessions.Where(x => x.UserId == userId).ToListAsync());
        Assert.Empty(await verification.Orders.Where(x => x.UserId == userId).ToListAsync());
        Assert.Single(await verification.CartItems.ToListAsync());
    }

    private sealed class ThrowAfterSaveInterceptor : SaveChangesInterceptor
    {
        private bool _thrown;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("Failure after SQL SaveChanges and before transaction commit.");
            }
            return base.SavedChangesAsync(eventData, result, cancellationToken);
        }
    }
}
