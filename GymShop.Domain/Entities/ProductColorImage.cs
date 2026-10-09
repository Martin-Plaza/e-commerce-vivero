namespace GymShop.Domain.Entities;

public class ProductColorImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? ProductAttributeOptionId { get; set; }
    public string Color { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public Product Product { get; set; } = null!;
    public ProductAttributeOption? ProductAttributeOption { get; set; }
}
