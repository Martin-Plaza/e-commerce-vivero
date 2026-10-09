using GymShop.Application.Abstractions;

namespace GymShop.Infrastructure.Services;

public sealed class OwnFleetShippingProvider : IShippingProvider
{
    public const string ProviderCode = "OwnFleet";
    private readonly IShippingSettings _settings;
    private readonly TimeProvider _timeProvider;

    public OwnFleetShippingProvider(IShippingSettings settings, TimeProvider timeProvider)
    {
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public string Code => ProviderCode;

    public ShippingProviderCapabilities Capabilities { get; } = new(
        SupportsHomeDelivery: true,
        SupportsPickupPoints: false,
        SupportsLabels: false,
        SupportsCancellation: false,
        SupportsTracking: false);

    public Task<IReadOnlyList<ShippingQuote>> QuoteAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.DeliveryType != ShippingDeliveryType.HomeDelivery)
            return Task.FromResult<IReadOnlyList<ShippingQuote>>([]);

        var now = _timeProvider.GetUtcNow();
        var from = now.AddDays(_settings.EstimatedDeliveryMinDays);
        var to = now.AddDays(_settings.EstimatedDeliveryMaxDays);
        IReadOnlyList<ShippingQuote> quotes =
        [
            new(
                ProviderCode,
                "standard",
                "Envío estándar",
                ShippingDeliveryType.HomeDelivery,
                _settings.HomeDeliveryCost,
                from,
                to,
                now.AddMinutes(_settings.QuoteLifetimeMinutes))
        ];
        return Task.FromResult(quotes);
    }

    public Task<IReadOnlyList<ShippingPickupPoint>> GetPickupPointsAsync(ShippingPickupPointRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShippingPickupPoint>>([]);

    public Task<ShippingCreationResult> CreateShipmentAsync(ShippingCreationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La creación de repartos propios se incorporará en la etapa de despachos.");

    public Task<ShippingLabel> GetLabelAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("El reparto propio no genera etiquetas.");

    public Task CancelShipmentAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La cancelación de repartos propios todavía no está automatizada.");

    public Task<ShippingTrackingResult> GetTrackingAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("El seguimiento de repartos propios todavía no está automatizado.");
}
