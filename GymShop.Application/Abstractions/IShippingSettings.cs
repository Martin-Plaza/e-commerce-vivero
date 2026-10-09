namespace GymShop.Application.Abstractions;

public interface IShippingSettings
{
    decimal HomeDeliveryCost { get; }
    string PickupAddress { get; }
    string PickupInstructions { get; }
    string PickupHours { get; }
    string OriginPostalCode => string.Empty;
    string OriginProvince => string.Empty;
    string OriginCity => string.Empty;
    string OriginStreet => string.Empty;
    string OriginStreetNumber => string.Empty;
    int QuoteLifetimeMinutes => 15;
    int EstimatedDeliveryMinDays => 1;
    int EstimatedDeliveryMaxDays => 3;
}

public sealed class FreeShippingSettings : IShippingSettings
{
    public decimal HomeDeliveryCost => 0;
    public string PickupAddress => string.Empty;
    public string PickupInstructions => string.Empty;
    public string PickupHours => string.Empty;
}

public interface IBankTransferSettings
{
    int PendingOrderLifetimeHours { get; }
}
