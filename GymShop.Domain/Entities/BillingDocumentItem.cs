namespace GymShop.Domain.Entities;

public class BillingDocumentItem
{
    public int Id { get; set; }
    public Guid BillingDocumentId { get; set; }
    public int? OrderItemId { get; set; }
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public BillingDocument BillingDocument { get; set; } = null!;
    public OrderItem? OrderItem { get; set; }
}
