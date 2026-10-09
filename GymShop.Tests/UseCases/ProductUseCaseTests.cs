using GymShop.Application.Common;
using GymShop.Application.DTOs.Products;
using GymShop.Application.DTOs.Stock;
using GymShop.Application.UseCases.Products;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public class ProductUseCaseTests
{
    [Fact]
    public async Task CreateProduct_persists_product()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var useCase = new CreateProductUseCase(db);

        var result = await useCase.ExecuteAsync(new CreateProductRequest(
            "Mancuerna",
            "Mancuerna 10kg",
            25000,
            5,
            "/images/mancuerna.jpeg"
        ));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Mancuerna", result.Value.Name);
        Assert.Single(db.Products);
    }

    [Fact]
    public async Task CreateProduct_persists_packaged_dimensions()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest(
            "Banco regulable", "Listo para despachar", 150000, 2, "/images/banco.webp",
            PackageWeightGrams: 18500, PackageLengthCm: 120.5m, PackageWidthCm: 42m, PackageHeightCm: 25.25m));

        Assert.True(result.IsSuccess);
        var product = Assert.Single(db.Products);
        Assert.Equal(18500, product.PackageWeightGrams);
        Assert.Equal(120.5m, product.PackageLengthCm);
        Assert.True(result.Value!.HasCompletePackageDimensions);
    }

    [Fact]
    public async Task CreateProduct_rejects_partial_packaged_dimensions()
    {
        await using var db = await TestDbContextFactory.CreateAsync();

        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest(
            "Banco regulable", null, 150000, 2, "/images/banco.webp", PackageWeightGrams: 18500));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Empty(db.Products);
    }

    [Fact]
    public async Task CreateProduct_rejects_invalid_price()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var useCase = new CreateProductUseCase(db);

        var result = await useCase.ExecuteAsync(new CreateProductRequest("Producto", null, 0, 1, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
    }

    [Fact]
    public async Task CreateProduct_requires_image()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest("Producto", null, 10, 1, null));
        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Empty(db.Products);
    }

    [Fact]
    public async Task General_update_uses_current_stock_when_form_was_loaded_with_stale_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Producto", Description = "Anterior", Price = 100, Stock = 10 };
        db.Products.Add(product); await db.SaveChangesAsync();

        product.Stock = 9;
        await db.SaveChangesAsync();
        var result = await new UpdateProductUseCase(db).ExecuteAsync(product.Id,
            new UpdateProductRequest("Producto", "Descripción actualizada", 100, null, true));

        Assert.True(result.IsSuccess);
        Assert.Equal("Descripción actualizada", product.Description);
        Assert.Equal(9, product.Stock);
    }

    [Fact]
    public async Task Create_and_update_responses_include_selected_category()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var strength = new Category { Name = "Fuerza", Slug = "fuerza", DisplayOrder = 1 };
        var cardio = new Category { Name = "Cardio", Slug = "cardio", DisplayOrder = 2 };
        db.Categories.AddRange(strength, cardio);
        await db.SaveChangesAsync();

        var created = await new CreateProductUseCase(db).ExecuteAsync(
            new CreateProductRequest("Producto", "Descripción", 100, 5, "/images/producto.webp", strength.Id));

        Assert.True(created.IsSuccess);
        Assert.Equal("fuerza", created.Value?.Category?.Slug);

        var updated = await new UpdateProductUseCase(db).ExecuteAsync(
            created.Value!.Id,
            new UpdateProductRequest("Producto", "Descripción", 100, null, true, cardio.Id));

        Assert.True(updated.IsSuccess);
        Assert.Equal("cardio", updated.Value?.Category?.Slug);
        Assert.Equal(cardio.Id, db.Products.Single().CategoryId);
    }

    [Theory]
    [InlineData(151, 0, 0)]
    [InlineData(1, 1001, 0)]
    [InlineData(1, 0, 501)]
    public async Task CreateProduct_rejects_values_over_database_limits(int nameLength, int descriptionLength, int imageLength)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var request = new CreateProductRequest(
            new string('N', nameLength),
            descriptionLength == 0 ? null : new string('D', descriptionLength),
            10,
            1,
            imageLength == 0 ? null : "/" + new string('i', imageLength - 1));

        var result = await new CreateProductUseCase(db).ExecuteAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
        Assert.Empty(db.Products);
    }

    [Fact]
    public async Task Public_catalog_and_lookup_hide_inactive_products()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var active = new Product { Name = "Activo", Price = 10, Stock = 1, IsActive = true };
        var inactive = new Product { Name = "Inactivo", Price = 10, Stock = 1, IsActive = false };
        db.Products.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var catalog = await new GetProductsUseCase(db).ExecuteAsync(new ProductQuery(), false);
        var forcedPublicCatalog = await new GetProductsUseCase(db).ExecuteAsync(new ProductQuery(IncludeInactive: true), false);
        var publicLookup = await new GetProductByIdUseCase(db).ExecuteAsync(inactive.Id, false);

        Assert.Collection(catalog, item => Assert.Equal(active.Id, item.Id));
        Assert.Collection(forcedPublicCatalog, item => Assert.Equal(active.Id, item.Id));
        Assert.Equal(AppErrorType.NotFound, publicLookup.Error?.Type);
    }

    [Fact]
    public async Task Admin_can_list_and_get_inactive_products()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var inactive = new Product { Name = "Inactivo", Price = 10, Stock = 1, IsActive = false };
        db.Products.Add(inactive);
        await db.SaveChangesAsync();

        var catalog = await new GetProductsUseCase(db).ExecuteAsync(new ProductQuery(IncludeInactive: true), true);
        var lookup = await new GetProductByIdUseCase(db).ExecuteAsync(inactive.Id, true);

        Assert.Single(catalog);
        Assert.True(lookup.IsSuccess);
        Assert.Equal(inactive.Id, lookup.Value!.Id);
    }

    [Fact]
    public async Task Catalog_filters_by_search_category_stock_and_price()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var strength = new Category { Name = "Fuerza", Slug = "fuerza", DisplayOrder = 1 };
        var cardio = new Category { Name = "Cardio", Slug = "cardio", DisplayOrder = 2 };
        db.Categories.AddRange(strength, cardio);
        db.Products.AddRange(
            new Product { Name = "Mancuerna", Description = "Acero", Price = 100, Stock = 3, Category = strength },
            new Product { Name = "Soga", Description = "Cardio rápido", Price = 20, Stock = 0, Category = cardio });
        await db.SaveChangesAsync();

        var result = await new GetProductsUseCase(db).ExecuteAsync(
            new ProductQuery(Search: "acero", Category: "fuerza", InStock: true, MinPrice: 50, MaxPrice: 150), false);

        var product = Assert.Single(result);
        Assert.Equal("Mancuerna", product.Name);
        Assert.Equal("fuerza", product.Category?.Slug);
    }

    [Fact]
    public async Task Stock_and_status_changes_create_safe_audit_entries()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var actor = new User { Name = "Admin", Email = "product-admin@test.com", PasswordHash = "secret-hash", RoleId = 2, IsActive = true };
        var product = new Product { Name = "Producto", Price = 10, Stock = 5, IsActive = true };
        db.AddRange(actor, product);
        await db.SaveChangesAsync();
        var auditContext = new FakeAuditContext(actor.Id, "corr-product");

        var stock = await new AdjustStockUseCase(db, auditContext).ExecuteAsync(product.Id, new ManualStockAdjustmentRequest(3, "Reposición"));
        var status = await new UpdateProductStatusUseCase(db, auditContext).ExecuteAsync(product.Id, new UpdateProductStatusRequest(false));

        Assert.True(stock.IsSuccess);
        Assert.True(status.IsSuccess);
        Assert.Equal(["ProductStockAdjusted", "ProductStatusChanged"], db.AuditEntries.Select(x => x.Action).ToArray());
        Assert.All(db.AuditEntries, entry => Assert.Equal("corr-product", entry.CorrelationId));
        Assert.DoesNotContain("secret-hash", string.Join('|', db.AuditEntries.Select(x => x.OldValue + x.NewValue + x.Reason)));
    }

    [Fact]
    public async Task Failed_product_change_does_not_create_success_audit()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new AdjustStockUseCase(db, new FakeAuditContext(1, "corr-failed"))
            .ExecuteAsync(999999, new ManualStockAdjustmentRequest(4, "Reposición"));

        Assert.False(result.IsSuccess);
        Assert.Empty(db.AuditEntries);
    }
}
