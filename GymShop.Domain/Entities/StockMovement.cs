using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class StockMovement
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public int? ProductVariantId { get; set; }
    public StockMovementType Type { get; set; }
    public int Quantity { get; set; }
    public int PreviousStock { get; set; }
    public int ResultingStock { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int? ActorUserId { get; set; }
    public int? OrderId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Product Product { get; set; } = null!;
    public ProductVariant? ProductVariant { get; set; }
    public User? ActorUser { get; set; }
    public Order? Order { get; set; }
}
