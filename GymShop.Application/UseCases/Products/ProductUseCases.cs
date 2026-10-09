using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Products;

public interface IGetProductsUseCase
{
    Task<List<ProductResponse>> ExecuteAsync(ProductQuery query, bool canViewInactive, CancellationToken cancellationToken = default);
}

public interface IGetCategoriesUseCase
{
    Task<List<CategoryResponse>> ExecuteAsync(CancellationToken cancellationToken = default);
}

public interface IGetProductByIdUseCase
{
    Task<AppResult<ProductResponse>> ExecuteAsync(int id, bool canViewInactive, CancellationToken cancellationToken = default);
}

public interface ICreateProductUseCase
{
    Task<AppResult<ProductResponse>> ExecuteAsync(CreateProductRequest request, CancellationToken cancellationToken = default);
}

public interface IUpdateProductUseCase
{
    Task<AppResult<ProductResponse>> ExecuteAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default);
}

public interface IUpdateProductStatusUseCase
{
    Task<AppResult> ExecuteAsync(int id, UpdateProductStatusRequest request, CancellationToken cancellationToken = default);
}

public class GetProductsUseCase : IGetProductsUseCase
{
    private readonly IApplicationDbContext _db;

    public GetProductsUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductResponse>> ExecuteAsync(ProductQuery request, bool canViewInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<Product> query = _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Variants).ThenInclude(x => x.Attributes).ThenInclude(x => x.ProductAttributeOption).ThenInclude(x => x!.ProductAttribute).Include(x => x.ColorImages);
        if (!request.IncludeInactive || !canViewInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x => x.Name.ToLower().Contains(search) ||
                                     (x.Description != null && x.Description.ToLower().Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim().ToLower();
            query = query.Where(x => x.Category != null && x.Category.Slug == category);
        }
        if (request.InStock.HasValue)
            query = request.InStock.Value ? query.Where(x => x.Variants.Any() ? x.Variants.Any(v => v.IsActive && v.Stock > 0) : x.Stock > 0) : query.Where(x => x.Variants.Any() ? !x.Variants.Any(v => v.IsActive && v.Stock > 0) : x.Stock == 0);
        if (request.MinPrice.HasValue) query = query.Where(x => x.Price >= request.MinPrice.Value);
        if (request.MaxPrice.HasValue) query = query.Where(x => x.Price <= request.MaxPrice.Value);

        return await query
            .OrderByDescending(x => x.Id)
            .Select(x => ProductMapper.ToResponse(x))
            .ToListAsync(cancellationToken);
    }
}

public class GetCategoriesUseCase : IGetCategoriesUseCase
{
    private readonly IApplicationDbContext _db;
    public GetCategoriesUseCase(IApplicationDbContext db) => _db = db;

    public Task<List<CategoryResponse>> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _db.Categories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.Slug, x.Description, x.DisplayOrder, x.Color))
            .ToListAsync(cancellationToken);
}

public class GetProductByIdUseCase : IGetProductByIdUseCase
{
    private readonly IApplicationDbContext _db;

    public GetProductByIdUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<ProductResponse>> ExecuteAsync(int id, bool canViewInactive, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Variants).ThenInclude(x => x.Attributes).ThenInclude(x => x.ProductAttributeOption).ThenInclude(x => x!.ProductAttribute).Include(x => x.ColorImages)
            .SingleOrDefaultAsync(x => x.Id == id && (x.IsActive || canViewInactive), cancellationToken);
        return product is null
            ? AppResult<ProductResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado.")
            : AppResult<ProductResponse>.Success(ProductMapper.ToResponse(product));
    }
}

public class CreateProductUseCase : ICreateProductUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext _auditContext;

    public CreateProductUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        _db = db;
        _auditContext = auditContext ?? SystemAuditContext.Instance;
    }

    public async Task<AppResult<ProductResponse>> ExecuteAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ProductValidator.Validate(request.Name, request.Description, request.Price, request.Stock, request.ImageUrl);
        if (validationError is not null)
        {
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, validationError);
        }
        if (string.IsNullOrWhiteSpace(request.ImageUrl))
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, "Agregá una imagen o su URL.");
        var packageError = ProductValidator.ValidatePackage(request.PackageWeightGrams, request.PackageLengthCm, request.PackageWidthCm, request.PackageHeightCm);
        if (packageError is not null)
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, packageError);
        var options = await ProductVariantManager.LoadOptions(_db, request.Variants, cancellationToken);
        var variantsError = ProductVariantManager.Validate(request.Variants, options);
        if (variantsError is not null) return AppResult<ProductResponse>.Failure(AppErrorType.Validation, variantsError);
        var colorImagesError = ProductVariantManager.ValidateColorImages(request.ColorImages, options);
        if (colorImagesError is not null) return AppResult<ProductResponse>.Failure(AppErrorType.Validation, colorImagesError);

        var category = request.CategoryId.HasValue
            ? await _db.Categories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.IsActive, cancellationToken)
            : null;
        if (request.CategoryId.HasValue && category is null)
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, "La categoría indicada no existe o está inactiva.");

        var product = new Product
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Price = request.Price,
            Stock = request.Stock,
            ImageUrl = request.ImageUrl?.Trim(),
            PackageWeightGrams = request.PackageWeightGrams,
            PackageLengthCm = request.PackageLengthCm,
            PackageWidthCm = request.PackageWidthCm,
            PackageHeightCm = request.PackageHeightCm,
            IsActive = true,
            Category = category
        };
        ProductVariantManager.Replace(product, request.Variants, options);
        ProductVariantManager.ReplaceColorImages(product, request.ColorImages);
        if (product.Variants.Count > 0) product.Stock = 0;

        _db.Products.Add(product);
        if (product.Variants.Count > 0)
        {
            foreach (var variant in product.Variants.Where(x => x.Stock > 0))
                StockMovementRecorder.Add(_db, product, StockMovementType.InitialStock, variant.Stock, 0,
                    "Stock inicial de la variante", _auditContext.ActorUserId, variant: variant);
        }
        else if (product.Stock > 0)
        {
            StockMovementRecorder.Add(_db, product, StockMovementType.InitialStock, product.Stock, 0,
                "Stock inicial del producto", _auditContext.ActorUserId);
        }
        await _db.SaveChangesAsync(cancellationToken);

        return AppResult<ProductResponse>.Success(ProductMapper.ToResponse(product));
    }
}

public class UpdateProductUseCase : IUpdateProductUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;

    public UpdateProductUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        _db = db;
        _auditContext = auditContext;
    }

    public async Task<AppResult<ProductResponse>> ExecuteAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.Include(x => x.Category).Include(x => x.Variants).ThenInclude(x => x.Attributes).ThenInclude(x => x.ProductAttributeOption).ThenInclude(x => x!.ProductAttribute).Include(x => x.ColorImages).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (product is null)
        {
            return AppResult<ProductResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado.");
        }

        var validationError = ProductValidator.ValidateGeneral(request.Name, request.Description, request.Price, request.ImageUrl);
        if (validationError is not null)
        {
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, validationError);
        }
        var packageError = ProductValidator.ValidatePackage(request.PackageWeightGrams, request.PackageLengthCm, request.PackageWidthCm, request.PackageHeightCm);
        if (packageError is not null)
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, packageError);
        var options = await ProductVariantManager.LoadOptions(_db, request.Variants, cancellationToken);
        var variantsError = ProductVariantManager.Validate(request.Variants, options);
        if (variantsError is not null) return AppResult<ProductResponse>.Failure(AppErrorType.Validation, variantsError);
        var colorImagesError = ProductVariantManager.ValidateColorImages(request.ColorImages, options);
        if (colorImagesError is not null) return AppResult<ProductResponse>.Failure(AppErrorType.Validation, colorImagesError);

        var category = request.CategoryId.HasValue
            ? await _db.Categories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && (x.IsActive || x.Id == product.CategoryId), cancellationToken)
            : null;
        if (request.CategoryId.HasValue && category is null)
            return AppResult<ProductResponse>.Failure(AppErrorType.Validation, "La categoría indicada no existe o está inactiva.");

        var oldValue = new { product.Name, product.Price, product.Stock, product.IsActive, product.PackageWeightGrams, product.PackageLengthCm, product.PackageWidthCm, product.PackageHeightCm };
        var previousVariantStocks = product.Variants.Where(x => x.Id > 0).ToDictionary(x => x.Id, x => x.Stock);
        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Price = request.Price;
        product.ImageUrl = request.ImageUrl?.Trim();
        product.PackageWeightGrams = request.PackageWeightGrams;
        product.PackageLengthCm = request.PackageLengthCm;
        product.PackageWidthCm = request.PackageWidthCm;
        product.PackageHeightCm = request.PackageHeightCm;
        product.IsActive = request.IsActive;
        product.Category = category;
        product.UpdatedAt = DateTime.UtcNow;
        ProductVariantManager.Replace(product, request.Variants, options);
        ProductVariantManager.ReplaceColorImages(product, request.ColorImages);
        if (product.Variants.Count > 0) product.Stock = 0;
        foreach (var variant in product.Variants.Where(x => x.IsActive))
        {
            var previous = variant.Id > 0 && previousVariantStocks.TryGetValue(variant.Id, out var stock) ? stock : 0;
            var delta = variant.Stock - previous;
            if (delta != 0)
                StockMovementRecorder.Add(_db, product, previous == 0 ? StockMovementType.InitialStock : StockMovementType.ManualCorrection,
                    delta, previous, previous == 0 ? "Stock inicial de la variante" : "Stock actualizado desde la edición del producto",
                    _auditContext?.ActorUserId, variant: variant);
        }
        AuditTrail.Add(_db, _auditContext, "ProductUpdated", "Product", product.Id, oldValue,
            new { product.Name, product.Price, product.Stock, product.IsActive, product.PackageWeightGrams, product.PackageLengthCm, product.PackageWidthCm, product.PackageHeightCm });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppResult<ProductResponse>.Failure(AppErrorType.Conflict, "El producto fue modificado por otra operacion. Volve a intentar.");
        }

        return AppResult<ProductResponse>.Success(ProductMapper.ToResponse(product));
    }
}

public class UpdateProductStatusUseCase : IUpdateProductStatusUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;

    public UpdateProductStatusUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        _db = db;
        _auditContext = auditContext;
    }

    public async Task<AppResult> ExecuteAsync(int id, UpdateProductStatusRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (product is null)
        {
            return AppResult.Failure(AppErrorType.NotFound, "Producto no encontrado.");
        }

        if (product.IsActive == request.IsActive) return AppResult.Success();

        var oldStatus = product.IsActive;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;
        AuditTrail.Add(_db, _auditContext, "ProductStatusChanged", "Product", product.Id,
            new { isActive = oldStatus }, new { isActive = product.IsActive });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppResult.Failure(AppErrorType.Conflict, "El producto fue modificado por otra operacion. Volve a intentar.");
        }

        return AppResult.Success();
    }
}

public static class ProductValidator
{
    public static string? Validate(string name, string? description, decimal price, int stock, string? imageUrl)
    {
        var generalError = ValidateGeneral(name, description, price, imageUrl);
        if (generalError is not null) return generalError;
        return stock < 0 ? "El stock no puede ser negativo." : null;
    }

    public static string? ValidateGeneral(string name, string? description, decimal price, string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "El nombre es obligatorio.";
        }

        if (name.Trim().Length > ValidationLimits.ProductName) return "El nombre no puede superar los 150 caracteres.";
        if (description?.Trim().Length > ValidationLimits.ProductDescription) return "La descripcion no puede superar los 1000 caracteres.";

        if (price <= 0)
        {
            return "El precio debe ser mayor a cero.";
        }

        if (price > 9999999999999999.99m || decimal.Round(price, 2) != price)
        {
            return "El precio debe ser compatible con decimal(18,2).";
        }

        if (imageUrl?.Trim().Length > ValidationLimits.ImageUrl) return "ImageUrl no puede superar los 500 caracteres.";
        if (!new ProductImageUrlAttribute().IsValid(imageUrl?.Trim()))
        {
            return "ImageUrl debe ser una URL http/https o una ruta web local valida.";
        }

        return null;
    }

    public static string? ValidatePackage(int? weightGrams, decimal? lengthCm, decimal? widthCm, decimal? heightCm)
    {
        var values = new decimal?[] { weightGrams, lengthCm, widthCm, heightCm };
        if (values.All(x => x is null)) return null;
        if (values.Any(x => x is null)) return "Completá el peso y las tres dimensiones del producto empaquetado.";
        if (weightGrams is <= 0 or > 1_000_000) return "El peso empaquetado debe estar entre 1 y 1.000.000 gramos.";
        if (new[] { lengthCm!.Value, widthCm!.Value, heightCm!.Value }.Any(x => x <= 0 || x > 1000 || decimal.Round(x, 2) != x))
            return "Cada dimensión empaquetada debe estar entre 0,01 y 1.000 cm, con hasta dos decimales.";
        return null;
    }
}

internal static class ProductMapper
{
    public static ProductResponse ToResponse(Product product)
    {
        return new ProductResponse(
            product.Id,
            product.Name,
            product.Description,
            product.Variants.Count > 0 ? product.Variants.Where(x => x.IsActive).Select(x => x.Price ?? product.Price).DefaultIfEmpty(product.Price).Min() : product.Price,
            product.Variants.Count > 0 ? product.Variants.Where(x => x.IsActive).Sum(x => x.Stock) : product.Stock,
            product.ImageUrl,
            product.IsActive,
            product.Category is null ? null : new CategorySummaryResponse(product.Category.Id, product.Category.Name, product.Category.Slug),
            product.Variants.OrderBy(x => x.Id).Select(x => new ProductVariantResponse(x.Id, x.Sku, x.Price ?? product.Price, x.Stock, x.IsActive,
                x.Attributes.OrderBy(a => a.Name).ToDictionary(a => a.Name, a => a.Value), x.Attributes.Where(a => a.ProductAttributeOptionId.HasValue).Select(a => a.ProductAttributeOptionId!.Value).ToList(),
                x.PackageWeightGrams, x.PackageLengthCm, x.PackageWidthCm, x.PackageHeightCm,
                (x.PackageWeightGrams ?? product.PackageWeightGrams).HasValue
                    && (x.PackageLengthCm ?? product.PackageLengthCm).HasValue
                    && (x.PackageWidthCm ?? product.PackageWidthCm).HasValue
                    && (x.PackageHeightCm ?? product.PackageHeightCm).HasValue)).ToList(),
            product.ColorImages.SelectMany(x => x.ProductAttributeOptionId.HasValue ? new[] { new KeyValuePair<string,string>(x.ProductAttributeOptionId.Value.ToString(), x.ImageUrl), new KeyValuePair<string,string>(x.Color, x.ImageUrl) } : new[] { new KeyValuePair<string,string>(x.Color, x.ImageUrl) }).GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First().Value, StringComparer.OrdinalIgnoreCase),
            product.Variants.SelectMany(v => v.Attributes).Where(x => x.ProductAttributeOption != null).GroupBy(x => x.ProductAttributeOption!.ProductAttribute)
                .OrderBy(g => g.Key.DisplayOrder).ThenBy(g => g.Key.Name).Select(g => new ProductAttributeSelectionResponse(g.Key.Id, g.Key.Name, g.Key.Presentation, g.Key.DisplayOrder,
                    g.Select(x => x.ProductAttributeOption!).DistinctBy(x => x.Id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Value).Select(x => new ProductAttributeOptionSelectionResponse(x.Id, x.Value, x.VisualValue, x.DisplayOrder)).ToList())).ToList(),
            product.PackageWeightGrams,
            product.PackageLengthCm,
            product.PackageWidthCm,
            product.PackageHeightCm,
            product.PackageWeightGrams.HasValue && product.PackageLengthCm.HasValue && product.PackageWidthCm.HasValue && product.PackageHeightCm.HasValue
        );
    }
}

internal static class ProductVariantManager
{
    public static async Task<List<ProductAttributeOption>> LoadOptions(IApplicationDbContext db, List<ProductVariantInput>? variants, CancellationToken ct)
    {
        var ids = (variants ?? []).SelectMany(x => x.OptionIds ?? []).Distinct().ToList();
        return await db.ProductAttributeOptions.Include(x => x.ProductAttribute).Where(x => ids.Contains(x.Id)).ToListAsync(ct);
    }
    public static string? Validate(List<ProductVariantInput>? variants, List<ProductAttributeOption> options)
    {
        if (variants is null || variants.Count == 0) return null;
        if (variants.Any(v => string.IsNullOrWhiteSpace(v.Sku))) return "Cada variante debe tener SKU.";
        if (variants.GroupBy(v => v.Sku.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) return "Los SKU de variantes no pueden repetirse.";
        if (variants.Any(v => v.Price is <= 0 || v.Stock < 0)) return "Precio y stock de variante no son válidos.";
        foreach (var variant in variants)
        {
            var packageError = ValidatePackageOverrides(variant);
            if (packageError is not null) return packageError;
        }
        if (variants.All(v => (v.OptionIds is null || v.OptionIds.Count == 0) && v.Attributes is { Count: > 0 })) {
            var legacy = variants.Select(v => string.Join("|", v.Attributes!.OrderBy(a => a.Key).Select(a => $"{a.Key.Trim().ToLowerInvariant()}={a.Value.Trim().ToLowerInvariant()}")));
            return legacy.Distinct().Count() == variants.Count ? null : "No puede haber combinaciones de atributos repetidas.";
        }
        if (variants.Any(v => v.OptionIds is null || v.OptionIds.Count == 0)) return "Cada variante debe seleccionar opciones de atributos.";
        var optionMap = options.ToDictionary(x => x.Id);
        if (variants.SelectMany(v => v.OptionIds!).Any(id => !optionMap.TryGetValue(id, out var option) || !option.IsActive || !option.ProductAttribute.IsActive)) return "Una opción seleccionada no existe o está inactiva.";
        if (variants.Any(v => v.OptionIds!.Distinct().Count() != v.OptionIds!.Count || v.OptionIds.GroupBy(id => optionMap[id].ProductAttributeId).Any(g => g.Count() > 1))) return "Cada combinación debe tener una sola opción por atributo.";
        var signature = variants[0].OptionIds!.Select(id => optionMap[id].ProductAttributeId).Order().ToArray();
        if (variants.Any(v => !v.OptionIds!.Select(id => optionMap[id].ProductAttributeId).Order().SequenceEqual(signature))) return "Todas las variantes deben usar los mismos atributos.";
        var combinations = variants.Select(v => string.Join("|", v.OptionIds!.Order()));
        return combinations.Distinct().Count() == variants.Count ? null : "No puede haber combinaciones de atributos repetidas.";
    }

    public static void Replace(Product product, List<ProductVariantInput>? inputs, List<ProductAttributeOption> options)
    {
        inputs ??= [];
        var wanted = inputs.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();
        foreach (var removed in product.Variants.Where(x => x.Id > 0 && !wanted.Contains(x.Id)).ToList()) removed.IsActive = false;
        foreach (var input in inputs)
        {
            var variant = input.Id.HasValue ? product.Variants.SingleOrDefault(x => x.Id == input.Id) : null;
            if (variant is null) { variant = new ProductVariant(); product.Variants.Add(variant); }
            variant.Sku = input.Sku.Trim(); variant.Price = input.Price; variant.Stock = input.Stock; variant.IsActive = input.IsActive;
            variant.PackageWeightGrams = input.PackageWeightGrams;
            variant.PackageLengthCm = input.PackageLengthCm;
            variant.PackageWidthCm = input.PackageWidthCm;
            variant.PackageHeightCm = input.PackageHeightCm;
            variant.Attributes.Clear();
            if (input.OptionIds is { Count: > 0 }) foreach (var option in input.OptionIds.Select(id => options.Single(x => x.Id == id)).OrderBy(x => x.ProductAttribute.DisplayOrder)) variant.Attributes.Add(new ProductVariantAttribute { ProductAttributeOptionId = option.Id, Name = option.ProductAttribute.Name, Value = option.Value });
            else foreach (var attribute in input.Attributes!.OrderBy(x => x.Key)) variant.Attributes.Add(new ProductVariantAttribute { Name = attribute.Key.Trim(), Value = attribute.Value.Trim() });
        }
    }

    private static string? ValidatePackageOverrides(ProductVariantInput variant)
    {
        if (variant.PackageWeightGrams is <= 0 or > 1_000_000)
            return $"El peso empaquetado de la variante {variant.Sku} debe estar entre 1 y 1.000.000 gramos.";
        var dimensions = new[] { variant.PackageLengthCm, variant.PackageWidthCm, variant.PackageHeightCm };
        if (dimensions.Where(x => x.HasValue).Any(x => x <= 0 || x > 1000 || decimal.Round(x!.Value, 2) != x.Value))
            return $"Las dimensiones empaquetadas de la variante {variant.Sku} no son válidas.";
        return null;
    }

    public static string? ValidateColorImages(Dictionary<string, string>? images, List<ProductAttributeOption> options)
    {
        if (images is null) return null;
        var colors = options.Where(x => x.ProductAttribute.Presentation == "ColorSwatch").ToList();
        foreach (var (key, url) in images)
        {
            if (colors.Count > 0 && !colors.Any(x => x.Id.ToString() == key || x.Value.Equals(key, StringComparison.OrdinalIgnoreCase))) return "Las imágenes por color deben corresponder a colores del producto.";
            if (url.Trim().Length > ValidationLimits.ImageUrl || !new ProductImageUrlAttribute().IsValid(url.Trim())) return "Una imagen por color tiene una URL inválida.";
        }
        return null;
    }

    public static void ReplaceColorImages(Product product, Dictionary<string, string>? images)
    {
        images ??= new(StringComparer.OrdinalIgnoreCase);
        product.ColorImages.Clear();
        foreach (var (key, url) in images.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
            product.ColorImages.Add(new ProductColorImage { ProductAttributeOptionId = int.TryParse(key, out var id) ? id : null, Color = key, ImageUrl = url.Trim() });
    }
}


