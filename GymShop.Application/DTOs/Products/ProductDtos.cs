using System.ComponentModel.DataAnnotations;
using GymShop.Application.Common;

namespace GymShop.Application.DTOs.Products;

public record ProductResponse(
    int Id,
    string Name,
    string? Description,
    decimal Price,
    int Stock,
    string? ImageUrl,
    bool IsActive,
    CategorySummaryResponse? Category,
    List<ProductVariantResponse>? Variants = null,
    Dictionary<string, string>? ColorImages = null,
    List<ProductAttributeSelectionResponse>? ProductAttributes = null,
    int? PackageWeightGrams = null,
    decimal? PackageLengthCm = null,
    decimal? PackageWidthCm = null,
    decimal? PackageHeightCm = null,
    bool HasCompletePackageDimensions = false
);

public record ProductAttributeOptionSelectionResponse(int Id, string Value, string? VisualValue, int DisplayOrder);
public record ProductAttributeSelectionResponse(int Id, string Name, string Presentation, int DisplayOrder, List<ProductAttributeOptionSelectionResponse> Options);
public record ProductVariantResponse(int Id, string Sku, decimal Price, int Stock, bool IsActive, Dictionary<string, string> Attributes, List<int>? OptionIds = null,
    int? PackageWeightGrams = null, decimal? PackageLengthCm = null, decimal? PackageWidthCm = null, decimal? PackageHeightCm = null,
    bool HasCompletePackageDimensions = false);
public record ProductVariantInput(int? Id, [Required, StringLength(100)] string Sku, [SqlDecimal] decimal? Price,
    [Range(0, int.MaxValue)] int Stock, bool IsActive, Dictionary<string, string>? Attributes = null, List<int>? OptionIds = null,
    [Range(1, 1_000_000)] int? PackageWeightGrams = null,
    [Range(0.01, 1000)] decimal? PackageLengthCm = null,
    [Range(0.01, 1000)] decimal? PackageWidthCm = null,
    [Range(0.01, 1000)] decimal? PackageHeightCm = null);

public record CategorySummaryResponse(int Id, string Name, string Slug);

public record CategoryResponse(int Id, string Name, string Slug, string? Description, int DisplayOrder, string? Color = null);

public record ProductQuery(
    string? Search = null,
    string? Category = null,
    bool? InStock = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    bool IncludeInactive = false);

public record CreateProductRequest(
    [Required, StringLength(ValidationLimits.ProductName)] string Name,
    [StringLength(ValidationLimits.ProductDescription)] string? Description,
    [SqlDecimal] decimal Price,
    [Range(0, int.MaxValue)] int Stock,
    [Required(ErrorMessage = "Agregá una imagen o su URL."), StringLength(ValidationLimits.ImageUrl), ProductImageUrl] string? ImageUrl,
    int? CategoryId = null,
    List<ProductVariantInput>? Variants = null,
    Dictionary<string, string>? ColorImages = null,
    [Range(1, 1_000_000)] int? PackageWeightGrams = null,
    [Range(0.01, 1000)] decimal? PackageLengthCm = null,
    [Range(0.01, 1000)] decimal? PackageWidthCm = null,
    [Range(0.01, 1000)] decimal? PackageHeightCm = null
);

public record UpdateProductRequest(
    [Required, StringLength(ValidationLimits.ProductName)] string Name,
    [StringLength(ValidationLimits.ProductDescription)] string? Description,
    [SqlDecimal] decimal Price,
    [StringLength(ValidationLimits.ImageUrl), ProductImageUrl] string? ImageUrl,
    bool IsActive,
    int? CategoryId = null,
    List<ProductVariantInput>? Variants = null,
    Dictionary<string, string>? ColorImages = null,
    [Range(1, 1_000_000)] int? PackageWeightGrams = null,
    [Range(0.01, 1000)] decimal? PackageLengthCm = null,
    [Range(0.01, 1000)] decimal? PackageWidthCm = null,
    [Range(0.01, 1000)] decimal? PackageHeightCm = null
);

public record UpdateProductStatusRequest(bool IsActive);

