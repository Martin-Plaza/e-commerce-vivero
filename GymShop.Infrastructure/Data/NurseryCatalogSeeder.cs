using GymShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymShop.Infrastructure.Data;

internal static class NurseryCatalogSeeder
{
    private sealed record CategorySeed(string Name, string Slug, string Description, string Color, int DisplayOrder);
    private sealed record ProductSeed(
        string Name,
        string Description,
        decimal Price,
        int Stock,
        string? ImageUrl,
        string CategorySlug,
        int WeightGrams,
        decimal LengthCm,
        decimal WidthCm,
        decimal HeightCm);

    private static readonly CategorySeed[] Categories =
    [
        new("Plantas de interior", "plantas-interior", "Verde que transforma cada rincón de tu casa.", "#315C45", 1),
        new("Plantas de exterior", "plantas-exterior", "Especies resistentes para balcones, patios y jardines.", "#6F8F55", 2),
        new("Aromáticas y huerta", "aromaticas-huerta", "Cultivá aromas y sabores frescos todos los días.", "#A66B3F", 3),
        new("Cactus y suculentas", "cactus-suculentas", "Pequeñas, nobles y llenas de carácter.", "#9AAA72", 4),
        new("Macetas y accesorios", "macetas-accesorios", "Objetos funcionales para cuidar y lucir tus plantas.", "#C57955", 5),
        new("Sustratos y cuidado", "sustratos-cuidado", "Todo lo necesario para raíces sanas y crecimiento sostenido.", "#C89A4B", 6),
    ];

    private static readonly ProductSeed[] Products =
    [
        new("Monstera deliciosa", "Una planta tropical protagonista, de hojas amplias y caladas. Ideal para interiores luminosos sin sol directo.", 39900m, 14, "/images/products/monstera-deliciosa.png", "plantas-interior", 5200, 34, 34, 85),
        new("Potus limón", "Enredadera resistente y de crecimiento rápido, perfecta para estantes altos o macetas colgantes.", 24500m, 21, "/images/products/regadera-savia.png", "plantas-interior", 1800, 22, 22, 42),
        new("Lavanda francesa", "Aromática de floración violeta y perfume intenso. Disfruta del sol y los espacios bien ventilados.", 18900m, 18, "/images/products/lavanda.png", "plantas-exterior", 2300, 24, 24, 46),
        new("Romero compacto", "Aromática perenne para cocinar y perfumar. Requiere buen sol y riego moderado.", 12800m, 26, "/images/products/romero.png", "aromaticas-huerta", 1900, 22, 22, 38),
        new("Trío de suculentas", "Selección de tres variedades de bajo mantenimiento en macetas de cerámica color arena.", 22500m, 17, "/images/products/suculentas.png", "cactus-suculentas", 2100, 36, 18, 20),
        new("Sustrato orgánico premium 20 L", "Mezcla aireada con compost, perlita y fibra vegetal para plantas de interior y exterior.", 15900m, 35, "/images/products/sustrato-organico.png", "sustratos-cuidado", 8500, 48, 32, 14),
        new("Regadera Savia 5 L", "Regadera metálica en verde salvia, con pico largo para un riego preciso y suave.", 28900m, 11, "/images/products/regadera-savia.png", "macetas-accesorios", 1200, 52, 22, 34),
        new("Maceta terracota clásica 24 cm", "Maceta de barro poroso con plato incluido. Favorece la respiración de las raíces.", 16500m, 24, "/images/products/lavanda.png", "macetas-accesorios", 3100, 28, 28, 27),
    ];

    public static async Task SeedAsync(
        GymShopDbContext db,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("DemoCatalog:Enabled")) return;

        var nurserySlugs = Categories.Select(category => category.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nurseryProductNames = Products.Select(product => product.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingCategories = await db.Categories.ToListAsync(cancellationToken);
        foreach (var category in existingCategories.Where(category => !nurserySlugs.Contains(category.Slug)))
        {
            category.IsActive = false;
        }

        var existingProducts = await db.Products.ToListAsync(cancellationToken);
        foreach (var product in existingProducts.Where(product => !nurseryProductNames.Contains(product.Name)))
        {
            product.IsActive = false;
            product.Stock = 0;
            product.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var seed in Categories)
        {
            var category = existingCategories.FirstOrDefault(item => item.Slug == seed.Slug);
            if (category is null)
            {
                category = new Category { Slug = seed.Slug };
                db.Categories.Add(category);
                existingCategories.Add(category);
            }

            category.Name = seed.Name;
            category.Description = seed.Description;
            category.Color = seed.Color;
            category.DisplayOrder = seed.DisplayOrder;
            category.IsActive = true;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var seed in Products)
        {
            var product = existingProducts.FirstOrDefault(item => item.Name == seed.Name);
            if (product is null)
            {
                product = new Product { Name = seed.Name, CreatedAt = DateTime.UtcNow };
                db.Products.Add(product);
                existingProducts.Add(product);
            }

            product.Description = seed.Description;
            product.Price = seed.Price;
            product.Stock = seed.Stock;
            product.ImageUrl = seed.ImageUrl;
            product.CategoryId = existingCategories.Single(category => category.Slug == seed.CategorySlug).Id;
            product.PackageWeightGrams = seed.WeightGrams;
            product.PackageLengthCm = seed.LengthCm;
            product.PackageWidthCm = seed.WidthCm;
            product.PackageHeightCm = seed.HeightCm;
            product.IsActive = true;
            product.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
