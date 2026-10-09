namespace GymShop.Application.Abstractions;

public interface IShippingProvider
{
    string Code { get; }
    ShippingProviderCapabilities Capabilities { get; }
    Task<IReadOnlyList<ShippingQuote>> QuoteAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShippingPickupPoint>> GetPickupPointsAsync(ShippingPickupPointRequest request, CancellationToken cancellationToken = default);
    Task<ShippingCreationResult> CreateShipmentAsync(ShippingCreationRequest request, CancellationToken cancellationToken = default);
    Task<ShippingLabel> GetLabelAsync(string externalShipmentId, CancellationToken cancellationToken = default);
    Task CancelShipmentAsync(string externalShipmentId, CancellationToken cancellationToken = default);
    Task<ShippingTrackingResult> GetTrackingAsync(string externalShipmentId, CancellationToken cancellationToken = default);
}

public sealed record ShippingProviderCapabilities(
    bool SupportsHomeDelivery,
    bool SupportsPickupPoints,
    bool SupportsLabels,
    bool SupportsCancellation,
    bool SupportsTracking);

public sealed record ShippingAddress(
    string PostalCode,
    string Province,
    string City,
    string Street,
    string StreetNumber,
    string? Floor,
    string? Apartment,
    string? Notes);

public sealed record ShippingContact(string Name, string Email, string Phone);

public sealed record ShippingPackage(
    int WeightGrams,
    decimal LengthCm,
    decimal WidthCm,
    decimal HeightCm,
    decimal DeclaredValue,
    int Quantity = 1);

public sealed record ShippingQuoteRequest(
    ShippingAddress Origin,
    ShippingAddress Destination,
    IReadOnlyList<ShippingPackage> Packages,
    ShippingDeliveryType DeliveryType);

public sealed record ShippingQuote(
    string ProviderCode,
    string ServiceCode,
    string ServiceName,
    ShippingDeliveryType DeliveryType,
    decimal Price,
    DateTimeOffset? EstimatedDeliveryFrom,
    DateTimeOffset? EstimatedDeliveryTo,
    DateTimeOffset ExpiresAt);

public sealed record ShippingPickupPointRequest(string PostalCode, string? Province = null, string? City = null);
public sealed record ShippingPickupPoint(string Code, string Name, ShippingAddress Address);

public sealed record ShippingCreationRequest(
    string IdempotencyKey,
    string ExternalOrderId,
    string ServiceCode,
    ShippingDeliveryType DeliveryType,
    ShippingAddress Origin,
    ShippingContact Sender,
    ShippingAddress Destination,
    ShippingContact Recipient,
    IReadOnlyList<ShippingPackage> Packages,
    string? PickupPointCode = null);

public sealed record ShippingCreationResult(string ExternalShipmentId, string TrackingNumber, string? TrackingUrl);
public sealed record ShippingLabel(string ContentType, byte[] Content, string FileName);
public sealed record ShippingTrackingEvent(string ProviderStatus, string Description, DateTimeOffset OccurredAt);
public sealed record ShippingTrackingResult(string ProviderStatus, IReadOnlyList<ShippingTrackingEvent> Events);

public enum ShippingDeliveryType
{
    HomeDelivery = 1,
    PickupPoint = 2
}
