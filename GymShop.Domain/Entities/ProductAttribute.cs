namespace GymShop.Domain.Entities;

public class ProductAttribute
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Presentation { get; set; } = "Button";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<ProductAttributeOption> Options { get; set; } = new List<ProductAttributeOption>();
}
