namespace GymShop.Domain.Entities;

public class ProductVariant
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public int Stock { get; set; }
    public int? PackageWeightGrams { get; set; }
    public decimal? PackageLengthCm { get; set; }
    public decimal? PackageWidthCm { get; set; }
    public decimal? PackageHeightCm { get; set; }
    public bool IsActive { get; set; } = true;
    public uint RowVersion { get; set; }

    public Product Product { get; set; } = null!;
    public ICollection<ProductVariantAttribute> Attributes { get; set; } = new List<ProductVariantAttribute>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
}
