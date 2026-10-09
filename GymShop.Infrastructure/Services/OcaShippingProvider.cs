using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GymShop.Infrastructure.Services;

public sealed class OcaShippingProvider : IShippingProvider
{
    public const string ProviderCode = "OCA";
    private readonly HttpClient _client;
    private readonly OcaOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OcaShippingProvider> _logger;

    public OcaShippingProvider(
        HttpClient client,
        OcaOptions options,
        TimeProvider timeProvider,
        ILogger<OcaShippingProvider> logger)
    {
        _client = client;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Code => ProviderCode;

    public ShippingProviderCapabilities Capabilities { get; } = new(
        SupportsHomeDelivery: true,
        SupportsPickupPoints: false,
        SupportsLabels: false,
        SupportsCancellation: false,
        SupportsTracking: false);

    public async Task<IReadOnlyList<ShippingQuote>> QuoteAsync(ShippingQuoteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.DeliveryType != ShippingDeliveryType.HomeDelivery || request.Packages.Count == 0)
            return [];

        if (!TryGetNumericPostalCode(request.Origin.PostalCode, out var originPostalCode) ||
            !TryGetNumericPostalCode(request.Destination.PostalCode, out var destinationPostalCode))
            return [];

        var weightKg = request.Packages.Sum(package => (decimal)package.WeightGrams * package.Quantity) / 1000m;
        var volumeM3 = request.Packages.Sum(package =>
            package.LengthCm * package.WidthCm * package.HeightCm * package.Quantity) / 1_000_000m;
        var packageCount = request.Packages.Sum(package => package.Quantity);
        var declaredValue = request.Packages.Sum(package => package.DeclaredValue);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Cuit"] = _options.Cuit,
            ["Operativa"] = _options.Operativa.ToString(CultureInfo.InvariantCulture),
            ["PesoTotal"] = weightKg.ToString("0.###", CultureInfo.InvariantCulture),
            ["VolumenTotal"] = volumeM3.ToString("0.######", CultureInfo.InvariantCulture),
            ["CodigoPostalOrigen"] = originPostalCode,
            ["CodigoPostalDestino"] = destinationPostalCode,
            ["CantidadPaquetes"] = packageCount.ToString(CultureInfo.InvariantCulture),
            ["ValorDeclarado"] = decimal.Ceiling(declaredValue).ToString("0", CultureInfo.InvariantCulture)
        });

        HttpResponseMessage response;
        try
        {
            response = await _client.PostAsync(_options.QuoteEndpoint, content, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("OCA quote request timed out.");
            return [];
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "OCA quote request could not be completed.");
            return [];
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OCA quote request failed with status code {StatusCode}.", (int)response.StatusCode);
                return [];
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = ParseQuote(responseStream);
            if (parsed.Quote is null)
            {
                _logger.LogWarning(
                    "OCA quote response did not contain a valid tariff. Reason: {Reason}; ContentType: {ContentType}.",
                    parsed.FailureReason,
                    response.Content.Headers.ContentType?.MediaType ?? "unknown");
                return [];
            }

            var quoteData = parsed.Quote;
            var now = _timeProvider.GetUtcNow();
            var estimatedDelivery = now.AddDays(quoteData.DeliveryDays);
            var serviceName = string.IsNullOrWhiteSpace(quoteData.Scope)
                ? "OCA e-Pak"
                : $"OCA e-Pak · {quoteData.Scope}";

            return
            [
                new ShippingQuote(
                    ProviderCode,
                    $"operativa-{_options.Operativa}-tipo-{quoteData.ServiceTypeId}",
                    serviceName,
                    ShippingDeliveryType.HomeDelivery,
                    quoteData.Total,
                    estimatedDelivery,
                    estimatedDelivery,
                    now.AddMinutes(_options.QuoteLifetimeMinutes))
            ];
        }
    }

    public Task<IReadOnlyList<ShippingPickupPoint>> GetPickupPointsAsync(ShippingPickupPointRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShippingPickupPoint>>([]);

    public Task<ShippingCreationResult> CreateShipmentAsync(ShippingCreationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La creación de envíos OCA se incorporará en la etapa operativa.");

    public Task<ShippingLabel> GetLabelAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Las etiquetas OCA se incorporarán en la etapa operativa.");

    public Task CancelShipmentAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La cancelación OCA se incorporará en la etapa operativa.");

    public Task<ShippingTrackingResult> GetTrackingAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("El seguimiento OCA se incorporará en la etapa operativa.");

    private static OcaQuoteParseResult ParseQuote(Stream xml)
    {
        try
        {
            using var xmlReader = XmlReader.Create(xml, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1_000_000
            });
            var document = XDocument.Load(xmlReader, LoadOptions.None);
            var providerError = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "Error")?.Value;
            if (!string.IsNullOrWhiteSpace(providerError))
            {
                var reason = providerError.Contains("CUIT", StringComparison.OrdinalIgnoreCase) ||
                             providerError.Contains("operativa", StringComparison.OrdinalIgnoreCase)
                    ? "invalid_cuit_or_operativa"
                    : "provider_rejected_request";
                return new OcaQuoteParseResult(null, reason);
            }

            var row = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Table");
            if (row is null) return new OcaQuoteParseResult(null, "missing_tariff");

            var totalText = Value(row, "Total");
            var deliveryText = Value(row, "PlazoEntrega");
            var serviceTypeText = Value(row, "idTiposervicio");
            if (!decimal.TryParse(totalText, NumberStyles.Number, CultureInfo.InvariantCulture, out var total) || total < 0 ||
                !int.TryParse(deliveryText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var deliveryDays) || deliveryDays < 0 ||
                !int.TryParse(serviceTypeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serviceTypeId))
                return new OcaQuoteParseResult(null, "invalid_tariff_fields");

            return new OcaQuoteParseResult(
                new OcaQuoteData(total, deliveryDays, serviceTypeId, Value(row, "Ambito")),
                string.Empty);
        }
        catch (XmlException)
        {
            return new OcaQuoteParseResult(null, "invalid_xml");
        }
    }

    private static string? Value(XElement row, string name) =>
        row.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim();

    private static bool TryGetNumericPostalCode(string value, out string postalCode)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value?.Trim() ?? string.Empty, @"(?<!\d)\d{4}(?!\d)");
        postalCode = match.Success ? match.Value : string.Empty;
        return match.Success;
    }

    private sealed record OcaQuoteData(decimal Total, int DeliveryDays, int ServiceTypeId, string? Scope);
    private sealed record OcaQuoteParseResult(OcaQuoteData? Quote, string FailureReason);
}
