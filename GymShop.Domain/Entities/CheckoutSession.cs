using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class CheckoutSession
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? CartId { get; set; }
    public string? GuestFirstName { get; set; }
    public string? GuestLastName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public Guid? GuestAccessToken { get; set; }
    public int? OrderId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public CheckoutStatus Status { get; set; } = CheckoutStatus.AwaitingPayment;
    public decimal Total { get; set; }
    public decimal Subtotal { get; set; }
    public int? CouponId { get; set; }
    public string? CouponCode { get; set; }
    public decimal DiscountAmount { get; set; }
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.HomeDelivery;
    public decimal ShippingCost { get; set; }
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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
    public DateTime? CartClearedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public User? User { get; set; }
    public Cart? Cart { get; set; }
    public Coupon? Coupon { get; set; }
    public Order? Order { get; set; }
    public ICollection<CheckoutItem> Items { get; set; } = new List<CheckoutItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
