namespace GymShop.Domain.Entities;

public class CheckoutItem
{
    public long Id { get; set; }
    public int CheckoutSessionId { get; set; }
    public int ProductId { get; set; }
    public int? ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? VariantSku { get; set; }
    public string? VariantAttributesJson { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal { get; set; }

    public CheckoutSession CheckoutSession { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
}
