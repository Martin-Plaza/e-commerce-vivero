namespace GymShop.Domain.Entities;

public class ShippingQuoteReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public int CartId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CartFingerprint { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string StreetNumber { get; set; } = string.Empty;
    public string? Floor { get; set; }
    public string? Apartment { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
}
