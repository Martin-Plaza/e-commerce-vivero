using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Products;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GymShop.Tests.UseCases;

public sealed class ProductVariantUseCaseTests
{
    [Fact]
    public async Task Creating_variants_records_initial_stock_movements_per_combination()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var size = new ProductAttribute { Name = "Talle", Options = { new ProductAttributeOption { Value = "M" }, new ProductAttributeOption { Value = "L" } } }; db.ProductAttributes.Add(size); await db.SaveChangesAsync(); var options = size.Options.OrderBy(x => x.Id).ToList();
        var variants = new List<ProductVariantInput> { new(null, "REM-M", null, 2, true, null, [options[0].Id]), new(null, "REM-L", null, 4, true, null, [options[1].Id]) };

        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest("Remera", null, 100, 0, "/general.webp", null, variants));

        Assert.True(result.IsSuccess); var movements = await db.StockMovements.OrderBy(x => x.ProductVariant!.Sku).ToListAsync(); Assert.Equal(2, movements.Count); Assert.Equal([4, 2], movements.Select(x => x.Quantity).ToArray()); Assert.All(movements, x => Assert.Equal("InitialStock", x.Type.ToString()));
    }

    [Fact]
    public async Task Editing_variant_stock_records_delta_and_keeps_history_consistent()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var size = new ProductAttribute { Name = "Talle", Options = { new ProductAttributeOption { Value = "M" } } }; var product = new Product { Name = "Remera", Price = 100, ImageUrl = "/general.webp", Variants = { new ProductVariant { Sku = "REM-M", Stock = 2 } } }; db.AddRange(size, product); await db.SaveChangesAsync(); var option = size.Options.Single(); var variant = product.Variants.Single(); variant.Attributes.Add(new ProductVariantAttribute { ProductAttributeOptionId = option.Id, Name = "Talle", Value = "M" }); await db.SaveChangesAsync();
        var input = new List<ProductVariantInput> { new(variant.Id, variant.Sku, null, 5, true, null, [option.Id]) };

        var result = await new UpdateProductUseCase(db).ExecuteAsync(product.Id, new("Remera", null, 100, "/general.webp", true, null, input));

        Assert.True(result.IsSuccess); var movement = Assert.Single(db.StockMovements); Assert.Equal(3, movement.Quantity); Assert.Equal(2, movement.PreviousStock); Assert.Equal(5, movement.ResultingStock); Assert.Equal(variant.Id, movement.ProductVariantId);
    }

    [Fact]
    public async Task Variant_stock_edit_and_movement_are_atomic_when_persistence_fails()
    {
        var database = $"variant-stock-atomic-{Guid.NewGuid():N}"; var normal = new DbContextOptionsBuilder<GymShop.Infrastructure.Data.GymShopDbContext>().UseInMemoryDatabase(database).Options;
        int productId, optionId, variantId;
        await using (var seed = new GymShop.Infrastructure.Data.GymShopDbContext(normal)) { var size = new ProductAttribute { Name = "Talle", Options = { new ProductAttributeOption { Value = "M" } } }; var product = new Product { Name = "Remera", Price = 100, ImageUrl = "/general.webp", Variants = { new ProductVariant { Sku = "REM-M", Stock = 2 } } }; seed.AddRange(size, product); await seed.SaveChangesAsync(); var option = size.Options.Single(); var variant = product.Variants.Single(); variant.Attributes.Add(new ProductVariantAttribute { ProductAttributeOptionId = option.Id, Name = "Talle", Value = "M" }); await seed.SaveChangesAsync(); productId = product.Id; optionId = option.Id; variantId = variant.Id; }
        var failing = new DbContextOptionsBuilder<GymShop.Infrastructure.Data.GymShopDbContext>().UseInMemoryDatabase(database).AddInterceptors(new RejectVariantMovementInterceptor()).Options;
        await using (var db = new GymShop.Infrastructure.Data.GymShopDbContext(failing)) { var request = new UpdateProductRequest("Remera", null, 100, "/general.webp", true, null, [new(variantId, "REM-M", null, 7, true, null, [optionId])]); await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateProductUseCase(db).ExecuteAsync(productId, request)); }
        await using var verify = new GymShop.Infrastructure.Data.GymShopDbContext(normal); Assert.Equal(2, (await verify.ProductVariants.SingleAsync()).Stock); Assert.Empty(verify.StockMovements);
    }
    [Fact]
    public async Task Product_stores_one_image_per_color_and_reuses_it_across_sizes()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var variants = new List<ProductVariantInput>
        {
            new(null, "REM-NEG-M", null, 2, true, new() { ["Color"] = "Negro", ["Talle"] = "M" }),
            new(null, "REM-NEG-L", null, 3, true, new() { ["Color"] = "Negro", ["Talle"] = "L" })
        };
        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest(
            "Remera", null, 100, 0, "/general.webp", null, variants, new() { ["Negro"] = "/negro.webp" }));

        Assert.True(result.IsSuccess);
        Assert.Equal("/negro.webp", result.Value!.ColorImages!["Negro"]);
        Assert.Single(await db.ProductColorImages.ToListAsync());
    }

    [Fact]
    public async Task Simple_product_keeps_existing_cart_behavior()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await User(db); var product = new Product { Name = "Simple", Price = 20, Stock = 3 };
        db.Products.Add(product); await db.SaveChangesAsync();

        var result = await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new(product.Id, 2));

        Assert.True(result.IsSuccess); Assert.Null(Assert.Single(result.Value!.Items).ProductVariantId);
    }

    [Fact]
    public async Task Variant_product_requires_a_valid_in_stock_combination()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await User(db); var product = ProductWithVariants(); db.Products.Add(product); await db.SaveChangesAsync();
        var valid = product.Variants.Single(x => x.Sku == "REM-M-NEGRO");

        var missing = await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new(product.Id, 1));
        var unknown = await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new(product.Id, 1, 999999));
        var validResult = await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new(product.Id, 1, valid.Id));

        Assert.Equal(AppErrorType.Validation, missing.Error?.Type);
        Assert.Equal(AppErrorType.Validation, unknown.Error?.Type);
        Assert.True(validResult.IsSuccess); Assert.Equal(valid.Id, Assert.Single(validResult.Value!.Items).ProductVariantId);
        Assert.Equal(120, validResult.Value.Items[0].UnitPrice);
    }

    [Fact]
    public async Task Checkout_snapshots_selected_variant_without_reserving_its_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await User(db); var product = ProductWithVariants(); db.Products.Add(product); await db.SaveChangesAsync();
        var selected = product.Variants.Single(x => x.Sku == "REM-M-NEGRO");
        await new AddCartItemUseCase(db).ExecuteAsync(user.Id, new(product.Id, 2, selected.Id));

        var result = await new CheckoutCartUseCase(db, shippingSettings: new Shipping()).ExecuteAsync(user.Id, new("StorePickup", null, 0, 240, 0));

        Assert.True(result.IsSuccess); Assert.Equal(2, selected.Stock);
        Assert.Equal(4, product.Variants.Single(x => x.Sku == "REM-L-AZUL").Stock);
        var line = Assert.Single(result.Value!.Items); Assert.Equal("REM-M-NEGRO", line.VariantSku); Assert.Equal("M", line.VariantAttributes!["Talle"]);
    }

    private static Product ProductWithVariants() => new()
    {
        Name = "Remera", Price = 100, Stock = 0,
        Variants =
        {
            new ProductVariant { Sku = "REM-M-NEGRO", Price = 120, Stock = 2, Attributes = { new ProductVariantAttribute { Name = "Talle", Value = "M" }, new ProductVariantAttribute { Name = "Color", Value = "Negro" } } },
            new ProductVariant { Sku = "REM-L-AZUL", Stock = 4, Attributes = { new ProductVariantAttribute { Name = "Talle", Value = "L" }, new ProductVariantAttribute { Name = "Color", Value = "Azul" } } }
        }
    };

    private static async Task<User> User(GymShop.Infrastructure.Data.GymShopDbContext db)
    {
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = $"variant-{Guid.NewGuid():N}@test.com", Name = "Variant", PasswordHash = new PasswordHasher().Hash("123456"), Role = role, RoleId = role.Id };
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private sealed class Shipping : IShippingSettings
    {
        public decimal HomeDeliveryCost => 0;
        public string PickupAddress => "GymShop";
        public string PickupInstructions => "Retiro";
        public string PickupHours => "9 a 18";
    }

    private sealed class RejectVariantMovementInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<StockMovement>().Any(x => x.State == EntityState.Added) == true) throw new InvalidOperationException("movement failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
