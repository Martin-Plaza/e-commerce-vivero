using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string? GuestFirstName { get; set; }
    public string? GuestLastName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public Guid? GuestAccessToken { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool StockReserved { get; set; } = true;
    public string? CheckoutIdempotencyKey { get; set; }
    public string? CheckoutRequestFingerprint { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal Total { get; set; }
    public decimal Subtotal { get; set; }
    public string? CouponCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.HomeDelivery;
    public decimal ShippingCost { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Paid;
    public string ShippingAddress { get; set; } = string.Empty;
    public string? ShippingPostalCode { get; set; }
    public string? ShippingProvince { get; set; }
    public string? ShippingCity { get; set; }
    public string? ShippingStreet { get; set; }
    public string? ShippingStreetNumber { get; set; }
    public string? ShippingFloor { get; set; }
    public string? ShippingApartment { get; set; }
    public string? ShippingNotes { get; set; }
    public Guid? ShippingQuoteId { get; set; }
    public string? ShippingProviderCode { get; set; }
    public string? ShippingServiceCode { get; set; }
    public string? ShippingServiceName { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string PickupHours { get; set; } = string.Empty;
    public string PickupInstructions { get; set; } = string.Empty;
    public string? Carrier { get; set; }
    public string? TrackingNumber { get; set; }
    public string? TrackingUrl { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User? User { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public ICollection<BillingDocument> BillingDocuments { get; set; } = new List<BillingDocument>();
    public CouponRedemption? CouponRedemption { get; set; }
}


