using GymShop.Application.Common;
using GymShop.Application.DTOs.Categories;
using GymShop.Application.UseCases.Categories;
using GymShop.Application.UseCases.Products;
using GymShop.Domain.Entities;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public class CategoryAdminUseCaseTests
{
    [Fact]
    public async Task Public_and_admin_queries_apply_the_expected_visibility()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        db.Categories.AddRange(new Category { Name = "Activa", Slug = "activa", IsActive = true }, new Category { Name = "Oculta", Slug = "oculta", IsActive = false }); await db.SaveChangesAsync();
        var publicItems = await new GetCategoriesUseCase(db).ExecuteAsync();
        var adminItems = await new GetAdminCategoriesUseCase(db).ExecuteAsync();
        Assert.Single(publicItems); Assert.Equal(2, adminItems.Count);
    }

    [Fact]
    public async Task Create_normalizes_slug_and_rejects_duplicate_name_or_slug()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var useCase = new CreateCategoryUseCase(db);
        var created = await useCase.ExecuteAsync(new UpsertCategoryRequest("Fuerza Máxima", "Fuerza Máxima!", null, 2));
        Assert.True(created.IsSuccess); Assert.Equal("fuerza-maxima", created.Value!.Slug);
        Assert.Equal(AppErrorType.Conflict, (await useCase.ExecuteAsync(new UpsertCategoryRequest("fuerza máxima", "otra", null, 0))).Error!.Type);
        Assert.Equal(AppErrorType.Conflict, (await useCase.ExecuteAsync(new UpsertCategoryRequest("Otra", "FUERZA MAXIMA", null, 0))).Error!.Type);
    }

    [Fact]
    public async Task Category_color_is_persisted_and_exposed_to_storefront()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var created = await new CreateCategoryUseCase(db).ExecuteAsync(new UpsertCategoryRequest("Fuerza", "fuerza", null, 1, "#ff8a5b"));
        Assert.True(created.IsSuccess);
        Assert.Equal("#ff8a5b", created.Value!.Color);
        var publicItems = await new GetCategoriesUseCase(db).ExecuteAsync();
        Assert.Equal("#ff8a5b", Assert.Single(publicItems).Color);
        var invalid = await new CreateCategoryUseCase(db).ExecuteAsync(new UpsertCategoryRequest("Otra", "otra", null, 2, "#123456"));
        Assert.Equal(AppErrorType.Validation, invalid.Error?.Type);
    }

    [Theory]
    [InlineData("", "slug", null, 0)]
    [InlineData("Nombre", "---", null, 0)]
    [InlineData("Nombre", "slug", null, -1)]
    public async Task Create_rejects_invalid_values(string name, string slug, string? description, int order)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new CreateCategoryUseCase(db).ExecuteAsync(new UpsertCategoryRequest(name, slug, description, order));
        Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Update_and_status_preserve_product_relationship_and_are_audited()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var category = new Category { Name = "Fuerza", Slug = "fuerza", IsActive = true }; var product = new Product { Name = "Mancuerna", Price = 10, Stock = 1, Category = category }; db.Products.Add(product); await db.SaveChangesAsync();
        var updated = await new UpdateCategoryUseCase(db).ExecuteAsync(category.Id, new UpsertCategoryRequest("Pesas", "pesas", "Equipo", 3));
        var status = await new UpdateCategoryStatusUseCase(db).ExecuteAsync(category.Id, new UpdateCategoryStatusRequest(false));
        Assert.True(updated.IsSuccess); Assert.True(status.IsSuccess); Assert.Equal(category.Id, product.CategoryId); Assert.False(category.IsActive); Assert.Contains(db.AuditEntries, x => x.Action == "CategoryUpdated"); Assert.Contains(db.AuditEntries, x => x.Action == "CategoryStatusChanged");
    }

    [Fact]
    public async Task Update_returns_not_found_and_product_can_keep_current_inactive_category()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        Assert.Equal(AppErrorType.NotFound, (await new UpdateCategoryUseCase(db).ExecuteAsync(999, new UpsertCategoryRequest("X", "x", null, 0))).Error!.Type);
        var category = new Category { Name = "Vieja", Slug = "vieja", IsActive = false }; var product = new Product { Name = "Producto", Price = 10, Stock = 1, Category = category }; db.Products.Add(product); await db.SaveChangesAsync();
        var result = await new UpdateProductUseCase(db).ExecuteAsync(product.Id, new("Producto editado", null, 10, null, true, category.Id));
        Assert.True(result.IsSuccess); Assert.Equal(category.Id, db.Products.Single().CategoryId);
    }
}
