namespace GymShop.Domain.Entities;

public class ProductAttributeOption
{
    public int Id { get; set; }
    public int ProductAttributeId { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? VisualValue { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ProductAttribute ProductAttribute { get; set; } = null!;
    public ICollection<ProductVariantAttribute> VariantAttributes { get; set; } = new List<ProductVariantAttribute>();
    public ICollection<ProductColorImage> ColorImages { get; set; } = new List<ProductColorImage>();
}
