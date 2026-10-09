using GymShop.Application.Abstractions;

namespace GymShop.Infrastructure.Configuration;

public sealed class ShippingOptions : IShippingSettings
{
    public const string SectionName = "Shipping";
    public bool OwnFleetEnabled { get; set; } = true;
    public decimal HomeDeliveryCost { get; set; }
    public string PickupAddress { get; set; } = string.Empty;
    public string PickupInstructions { get; set; } = string.Empty;
    public string PickupHours { get; set; } = string.Empty;
    public string OriginPostalCode { get; set; } = string.Empty;
    public string OriginProvince { get; set; } = string.Empty;
    public string OriginCity { get; set; } = string.Empty;
    public string OriginStreet { get; set; } = string.Empty;
    public string OriginStreetNumber { get; set; } = string.Empty;
    public int QuoteLifetimeMinutes { get; set; } = 15;
    public int EstimatedDeliveryMinDays { get; set; } = 1;
    public int EstimatedDeliveryMaxDays { get; set; } = 3;
}
