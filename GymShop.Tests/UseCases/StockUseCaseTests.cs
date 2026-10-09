using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Stock;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;

namespace GymShop.Tests.UseCases;

public sealed class StockUseCaseTests
{
    [Fact]
    public async Task Positive_initial_stock_records_actor_quantity_and_balances()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var actor = new User { Name = "Admin", Email = "creator@test.com", PasswordHash = "x", RoleId = 2 };
        db.Users.Add(actor); await db.SaveChangesAsync();

        var result = await new GymShop.Application.UseCases.Products.CreateProductUseCase(db, new FakeAuditContext(actor.Id, "create"))
            .ExecuteAsync(new GymShop.Application.DTOs.Products.CreateProductRequest("Banco", null, 100, 7, "/images/banco.webp"));

        Assert.True(result.IsSuccess);
        var movement = Assert.Single(db.StockMovements);
        Assert.Equal(StockMovementType.InitialStock, movement.Type);
        Assert.Equal(7, movement.Quantity); Assert.Equal(0, movement.PreviousStock); Assert.Equal(7, movement.ResultingStock);
        Assert.Equal(actor.Id, movement.ActorUserId); Assert.Equal("Stock inicial del producto", movement.Reason);
    }

    [Fact]
    public async Task Zero_initial_stock_does_not_create_zero_movement()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new GymShop.Application.UseCases.Products.CreateProductUseCase(db)
            .ExecuteAsync(new GymShop.Application.DTOs.Products.CreateProductRequest("Banco", null, 100, 0, "/images/banco.webp"));
        Assert.True(result.IsSuccess); Assert.Empty(db.StockMovements);
    }

    [Fact]
    public async Task Product_and_initial_movement_are_atomic_when_movement_persistence_fails()
    {
        var database = $"initial-stock-atomic-{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<GymShop.Infrastructure.Data.GymShopDbContext>()
            .UseInMemoryDatabase(database).AddInterceptors(new RejectMovementSaveInterceptor()).Options;
        await using (var failing = new GymShop.Infrastructure.Data.GymShopDbContext(options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new GymShop.Application.UseCases.Products.CreateProductUseCase(failing)
                    .ExecuteAsync(new GymShop.Application.DTOs.Products.CreateProductRequest("Banco", null, 100, 2, "/images/banco.webp")));
        }
        await using var verification = new GymShop.Infrastructure.Data.GymShopDbContext(
            new DbContextOptionsBuilder<GymShop.Infrastructure.Data.GymShopDbContext>().UseInMemoryDatabase(database).Options);
        Assert.Empty(verification.Products); Assert.Empty(verification.StockMovements);
    }

    [Fact]
    public async Task Current_stock_can_be_reconstructed_from_movements()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var created = await new GymShop.Application.UseCases.Products.CreateProductUseCase(db)
            .ExecuteAsync(new GymShop.Application.DTOs.Products.CreateProductRequest("Banco", null, 100, 5, "/images/banco.webp"));
        await new AdjustStockUseCase(db).ExecuteAsync(created.Value!.Id, new(-2, "Conteo físico"));
        Assert.Equal(db.Products.Single().Stock, db.StockMovements.Where(x => x.ProductId == created.Value.Id).Sum(x => x.Quantity));
    }

    [Fact]
    public void Migration_creates_table_and_backfills_positive_existing_stock()
    {
        var migration = new GymShop.Infrastructure.Data.PostgresMigrations.AddStockMovements();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        typeof(GymShop.Infrastructure.Data.PostgresMigrations.AddStockMovements)
            .GetMethod("Up", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(migration, [builder]);

        Assert.Contains(builder.Operations.OfType<CreateTableOperation>(), x => x.Name == "StockMovements");
        var sql = Assert.Single(builder.Operations.OfType<SqlOperation>()).Sql;
        Assert.Contains("'InitialStock'", sql); Assert.Contains("WHERE \"Stock\" > 0", sql);
        Assert.Contains("Saldo inicial incorporado por migración", sql);
    }

    [Fact]
    public async Task Manual_adjustment_requires_reason_and_rejects_negative_result()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Disco", Price = 10, Stock = 3 };
        db.Products.Add(product); await db.SaveChangesAsync();
        var useCase = new AdjustStockUseCase(db, new FakeAuditContext(null, "stock-test"));

        var missingReason = await useCase.ExecuteAsync(product.Id, new(-1, " "));
        var negative = await useCase.ExecuteAsync(product.Id, new(-4, "Rotura"));

        Assert.False(missingReason.IsSuccess); Assert.False(negative.IsSuccess);
        Assert.Equal(3, product.Stock); Assert.Empty(db.StockMovements);
    }

    [Fact]
    public async Task Manual_adjustment_records_before_after_actor_and_reason()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var actor = new User { Name = "Admin", Email = "stock-admin@test.com", PasswordHash = "x", RoleId = 2 };
        var product = new Product { Name = "Disco", Price = 10, Stock = 3 };
        db.AddRange(actor, product); await db.SaveChangesAsync();

        var result = await new AdjustStockUseCase(db, new FakeAuditContext(actor.Id, "stock-test"))
            .ExecuteAsync(product.Id, new(5, "Recepción de mercadería"));

        Assert.True(result.IsSuccess); Assert.Equal(8, product.Stock);
        var movement = Assert.Single(db.StockMovements);
        Assert.Equal(3, movement.PreviousStock); Assert.Equal(8, movement.ResultingStock);
        Assert.Equal(5, movement.Quantity); Assert.Equal(actor.Id, movement.ActorUserId);
        Assert.Equal("Recepción de mercadería", movement.Reason);
    }

    [Fact]
    public async Task Variant_product_requires_variant_and_adjusts_only_selected_combination()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Remera", Price = 10, Stock = 0, Variants = { new ProductVariant { Sku = "REM-M", Stock = 2 }, new ProductVariant { Sku = "REM-L", Stock = 5 } } };
        db.Products.Add(product); await db.SaveChangesAsync(); var selected = product.Variants.Single(x => x.Sku == "REM-M");
        var useCase = new AdjustStockUseCase(db);

        var missing = await useCase.ExecuteAsync(product.Id, new(3, "Ingreso"));
        var adjusted = await useCase.ExecuteAsync(product.Id, new(3, "Ingreso", selected.Id));

        Assert.False(missing.IsSuccess); Assert.True(adjusted.IsSuccess); Assert.Equal(5, selected.Stock);
        Assert.Equal(5, product.Variants.Single(x => x.Sku == "REM-L").Stock);
        Assert.Equal(selected.Id, Assert.Single(db.StockMovements).ProductVariantId);
    }

    [Fact]
    public async Task Movement_query_filters_and_paginates()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Disco", Price = 10, Stock = 5 };
        db.Products.Add(product); await db.SaveChangesAsync();
        db.StockMovements.AddRange(
            new StockMovement { ProductId = product.Id, Type = StockMovementType.ManualEntry, Quantity = 2, PreviousStock = 1, ResultingStock = 3, Reason = "A", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1) },
            new StockMovement { ProductId = product.Id, Type = StockMovementType.ManualEntry, Quantity = 2, PreviousStock = 3, ResultingStock = 5, Reason = "B", CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var result = await new GetStockMovementsUseCase(db).ExecuteAsync(new(Page: 2, PageSize: 1,
            ProductId: product.Id, Type: "ManualEntry", FromUtc: DateTime.UtcNow.AddMinutes(-5), ToUtc: DateTime.UtcNow.AddMinutes(1)));

        Assert.True(result.IsSuccess); Assert.Equal(2, result.Value!.TotalItems); Assert.Equal(2, result.Value.TotalPages);
        Assert.Equal("A", Assert.Single(result.Value.Items).Reason);
    }

    [Fact]
    public async Task Checkout_does_not_create_stock_movements_before_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = new User { Name = "Cliente", Email = "buyer@test.com", PasswordHash = "x", RoleId = 1 };
        var product = new Product { Name = "Disco", Price = 10, Stock = 5 };
        var cart = new Cart { User = user }; cart.Items.Add(new CartItem { Product = product, Quantity = 2 });
        db.Add(cart); await db.SaveChangesAsync();

        var checkout = await new CheckoutCartUseCase(db).ExecuteAsync(user.Id, new CheckoutCartRequest("HomeDelivery", "Calle 123", 0));
        Assert.True(checkout.IsSuccess);
        Assert.Equal(5, product.Stock);
        Assert.Empty(db.StockMovements);
        Assert.Empty(db.Orders);
        Assert.Single(db.CheckoutSessions);
    }

    private sealed class RejectMovementSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StockMovement>().Any(x => x.State == EntityState.Added) == true)
                throw new InvalidOperationException("Movement persistence failed.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
