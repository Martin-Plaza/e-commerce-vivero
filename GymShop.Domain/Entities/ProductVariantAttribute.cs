namespace GymShop.Domain.Entities;

public class ProductVariantAttribute
{
    public int Id { get; set; }
    public int ProductVariantId { get; set; }
    public int? ProductAttributeOptionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ProductVariant ProductVariant { get; set; } = null!;
    public ProductAttributeOption? ProductAttributeOption { get; set; }
}
