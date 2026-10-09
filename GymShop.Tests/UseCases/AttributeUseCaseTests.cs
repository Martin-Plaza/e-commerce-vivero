using GymShop.Application.DTOs.Attributes;
using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Attributes;
using GymShop.Application.UseCases.Products;
using GymShop.Domain.Entities;
using GymShop.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Tests.UseCases;

public sealed class AttributeUseCaseTests
{
    [Fact]
    public async Task Admin_can_create_reusable_attribute_and_visual_options()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var service = new AttributeAdminService(db);
        var created = await service.CreateAsync(new UpsertAttributeRequest("Color", "ColorSwatch", 0), default);
        var option = await service.AddOptionAsync(created.Value!.Id, new UpsertAttributeOptionRequest("Azul", "#0066ff", 0), default);
        Assert.True(option.IsSuccess); Assert.Equal("#0066ff", option.Value!.VisualValue);
    }

    [Fact]
    public async Task Option_in_active_variant_cannot_be_disabled()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var attribute = new ProductAttribute { Name = "Talle", Options = { new ProductAttributeOption { Value = "M" } } }; db.ProductAttributes.Add(attribute); await db.SaveChangesAsync();
        var option = attribute.Options.Single(); db.Products.Add(new Product { Name = "Remera", Price = 10, Variants = { new ProductVariant { Sku = "R-M", Stock = 1, Attributes = { new ProductVariantAttribute { ProductAttributeOptionId = option.Id, Name = "Talle", Value = "M" } } } } }); await db.SaveChangesAsync();
        var result = await new AttributeAdminService(db).SetOptionStatusAsync(attribute.Id, option.Id, false, default);
        Assert.False(result.IsSuccess); Assert.True(option.IsActive);
    }

    [Fact]
    public async Task Product_accepts_only_catalog_options_and_preserves_product_color_image()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var color = new ProductAttribute { Name = "Color", Presentation = "ColorSwatch", Options = { new ProductAttributeOption { Value = "Negro", VisualValue = "#111111" } } }; db.ProductAttributes.Add(color); await db.SaveChangesAsync(); var negro = color.Options.Single();
        var variants = new List<ProductVariantInput> { new(null, "REM-NEG", null, 2, true, null, [negro.Id]) };
        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest("Remera", null, 100, 0, "/general.webp", null, variants, new() { [negro.Id.ToString()] = "/negro.webp" }));
        Assert.True(result.IsSuccess); Assert.Equal(negro.Id, (await db.ProductVariantAttributes.SingleAsync()).ProductAttributeOptionId); Assert.Equal(negro.Id, (await db.ProductColorImages.SingleAsync()).ProductAttributeOptionId);
    }

    [Fact]
    public async Task Product_rejects_unknown_or_inactive_option()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var variants = new List<ProductVariantInput> { new(null, "INVALID", null, 1, true, null, [999]) };
        var result = await new CreateProductUseCase(db).ExecuteAsync(new CreateProductRequest("Remera", null, 100, 0, "/general.webp", null, variants));
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Used_attribute_and_option_cannot_be_renamed_but_visual_value_can_change()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var attribute = new ProductAttribute { Name = "Color", Presentation = "ColorSwatch", Options = { new ProductAttributeOption { Value = "Negro", VisualValue = "#111111" } } }; db.ProductAttributes.Add(attribute); await db.SaveChangesAsync(); var option = attribute.Options.Single();
        db.Products.Add(new Product { Name = "Remera", Price = 10, Variants = { new ProductVariant { Sku = "R-N", Stock = 1, Attributes = { new ProductVariantAttribute { ProductAttributeOptionId = option.Id, Name = "Color", Value = "Negro" } } } } }); await db.SaveChangesAsync();
        var service = new AttributeAdminService(db);

        var attributeRename = await service.UpdateAsync(attribute.Id, new("Tono", "ColorSwatch", 0), default);
        var optionRename = await service.UpdateOptionAsync(attribute.Id, option.Id, new("Oscuro", "#222222", 0), default);
        var visualUpdate = await service.UpdateOptionAsync(attribute.Id, option.Id, new("Negro", "#000000", 0), default);

        Assert.False(attributeRename.IsSuccess); Assert.False(optionRename.IsSuccess); Assert.True(visualUpdate.IsSuccess);
        Assert.Equal("Color", attribute.Name); Assert.Equal("Negro", option.Value); Assert.Equal("#000000", option.VisualValue);
    }
}
