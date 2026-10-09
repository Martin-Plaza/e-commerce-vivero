using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GymShop.Infrastructure.Services;

public sealed class CorreoArgentinoShippingProvider : IShippingProvider
{
    public const string ProviderCode = "CorreoArgentino";
    private const int MaximumWeightGrams = 25_000;
    private const int MaximumDimensionCm = 150;
    private readonly HttpClient _client;
    private readonly CorreoArgentinoOptions _options;
    private readonly CorreoArgentinoTokenCache _tokenCache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CorreoArgentinoShippingProvider> _logger;

    public CorreoArgentinoShippingProvider(
        HttpClient client,
        CorreoArgentinoOptions options,
        CorreoArgentinoTokenCache tokenCache,
        TimeProvider timeProvider,
        ILogger<CorreoArgentinoShippingProvider> logger)
    {
        _client = client;
        _options = options;
        _tokenCache = tokenCache;
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

        var packages = BuildApiPackages(request.Packages);
        if (packages is null)
        {
            _logger.LogWarning("Correo Argentino quote skipped because a package exceeds provider limits.");
            return [];
        }

        var token = await GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return [];

        Dictionary<string, AggregatedRate>? aggregated = null;
        DateTimeOffset? providerExpiration = null;
        foreach (var packageGroup in packages)
        {
            var response = await RequestRatesAsync(token, request, packageGroup.Package, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _tokenCache.Invalidate(token);
                token = await GetTokenAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(token)) return [];
                response = await RequestRatesAsync(token, request, packageGroup.Package, cancellationToken);
            }

            if (!response.IsSuccess || response.Value is null) return [];
            if (response.Value.Rates is null) return [];
            var rates = response.Value.Rates
                .Where(rate => string.Equals(rate.DeliveredType, "D", StringComparison.OrdinalIgnoreCase) &&
                               !string.IsNullOrWhiteSpace(rate.ProductType) && rate.Price >= 0)
                .GroupBy(rate => rate.ProductType, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            if (rates.Count == 0) return [];

            if (aggregated is null)
            {
                aggregated = rates.ToDictionary(
                    pair => pair.Key,
                    pair => new AggregatedRate(pair.Value.ProductName, pair.Value.Price * packageGroup.Quantity),
                    StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                foreach (var unavailable in aggregated.Keys.Where(key => !rates.ContainsKey(key)).ToList())
                    aggregated.Remove(unavailable);
                foreach (var pair in aggregated)
                    pair.Value.Price += rates[pair.Key].Price * packageGroup.Quantity;
            }

            if (aggregated.Count == 0) return [];
            if (response.Value.ValidTo is { } validTo)
                providerExpiration = providerExpiration is null || validTo < providerExpiration ? validTo : providerExpiration;
        }

        if (aggregated is null || aggregated.Count == 0) return [];
        var now = _timeProvider.GetUtcNow();
        if (providerExpiration is { } expiredProviderValue && expiredProviderValue <= now)
        {
            _logger.LogWarning("Correo Argentino rates response contained an expired validity timestamp.");
            return [];
        }

        var localExpiration = now.AddMinutes(_options.QuoteLifetimeMinutes);
        var expiration = providerExpiration is { } providerValue && providerValue < localExpiration
            ? providerValue
            : localExpiration;
        var estimatedFrom = now.AddDays(_options.EstimatedDeliveryMinDays);
        var estimatedTo = now.AddDays(_options.EstimatedDeliveryMaxDays);

        return aggregated.Select(pair => new ShippingQuote(
                ProviderCode,
                $"{pair.Key.ToLowerInvariant()}-home",
                string.IsNullOrWhiteSpace(pair.Value.ProductName) ? "Correo Argentino" : pair.Value.ProductName,
                ShippingDeliveryType.HomeDelivery,
                pair.Value.Price,
                estimatedFrom,
                estimatedTo,
                expiration))
            .OrderBy(quote => quote.Price)
            .ToList();
    }

    public Task<IReadOnlyList<ShippingPickupPoint>> GetPickupPointsAsync(ShippingPickupPointRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ShippingPickupPoint>>([]);

    public Task<ShippingCreationResult> CreateShipmentAsync(ShippingCreationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La importación de envíos a MiCorreo se incorporará en la etapa operativa.");

    public Task<ShippingLabel> GetLabelAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Las etiquetas de Correo Argentino se incorporarán en la etapa operativa.");

    public Task CancelShipmentAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("La cancelación de Correo Argentino se incorporará en la etapa operativa.");

    public Task<ShippingTrackingResult> GetTrackingAsync(string externalShipmentId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("El seguimiento de Correo Argentino se incorporará en la etapa operativa.");

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken) =>
        await _tokenCache.GetAsync(_timeProvider, RequestTokenAsync, cancellationToken);

    private async Task<(string Token, DateTimeOffset ExpiresAt)?> RequestTokenAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ApiUsername}:{_options.ApiPassword}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        using var response = await SendAsync(request, "token", cancellationToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            if (response is not null)
                _logger.LogWarning("Correo Argentino token request failed with status code {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        TokenResponse? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Correo Argentino token response was not valid JSON.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(payload?.Token))
        {
            _logger.LogWarning("Correo Argentino token response was invalid.");
            return null;
        }

        var expiresAt = GetJwtExpiration(payload.Token) ?? _timeProvider.GetUtcNow().AddMinutes(10);
        return (payload.Token, expiresAt);
    }

    private async Task<ApiResult<RatesResponse>> RequestRatesAsync(
        string token,
        ShippingQuoteRequest quote,
        ApiPackage package,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rates")
        {
            Content = JsonContent.Create(new RatesRequest(
                _options.CustomerId,
                quote.Origin.PostalCode,
                quote.Destination.PostalCode,
                "D",
                new Dimensions(package.WeightGrams, package.HeightCm, package.WidthCm, package.LengthCm)))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, "rates", cancellationToken);
        if (response is null) return new ApiResult<RatesResponse>(false, null, 0);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Correo Argentino rates request failed with status code {StatusCode}.", (int)response.StatusCode);
            return new ApiResult<RatesResponse>(false, null, response.StatusCode);
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<RatesResponse>(cancellationToken: cancellationToken);
            return new ApiResult<RatesResponse>(value is not null, value, response.StatusCode);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Correo Argentino rates response was not valid JSON.");
            return new ApiResult<RatesResponse>(false, null, response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Correo Argentino {Operation} request timed out.", operation);
            return null;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Correo Argentino {Operation} request could not be completed.", operation);
            return null;
        }
    }

    private static IReadOnlyList<ApiPackageGroup>? BuildApiPackages(IReadOnlyList<ShippingPackage> packages)
    {
        var result = new Dictionary<ApiPackage, int>();
        foreach (var package in packages)
        {
            var apiPackage = new ApiPackage(
                package.WeightGrams,
                (int)decimal.Ceiling(package.LengthCm),
                (int)decimal.Ceiling(package.WidthCm),
                (int)decimal.Ceiling(package.HeightCm));
            if (apiPackage.WeightGrams is < 1 or > MaximumWeightGrams ||
                apiPackage.LengthCm is < 1 or > MaximumDimensionCm ||
                apiPackage.WidthCm is < 1 or > MaximumDimensionCm ||
                apiPackage.HeightCm is < 1 or > MaximumDimensionCm ||
                package.Quantity < 1)
                return null;

            result[apiPackage] = result.GetValueOrDefault(apiPackage) + package.Quantity;
        }

        return result.Select(pair => new ApiPackageGroup(pair.Key, pair.Value)).ToList();
    }

    private static DateTimeOffset? GetJwtExpiration(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            var value = parts[1].Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + (4 - value.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(value));
            return document.RootElement.TryGetProperty("exp", out var expiration) && expiration.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private sealed record ApiPackage(int WeightGrams, int LengthCm, int WidthCm, int HeightCm);
    private sealed record ApiPackageGroup(ApiPackage Package, int Quantity);
    private sealed record Dimensions(int Weight, int Height, int Width, int Length);
    private sealed record RatesRequest(
        string CustomerId,
        string PostalCodeOrigin,
        string PostalCodeDestination,
        string DeliveredType,
        Dimensions Dimensions);
    private sealed record TokenResponse(string Token);
    private sealed record Rate(string DeliveredType, string ProductType, string ProductName, decimal Price);
    private sealed record RatesResponse(string CustomerId, DateTimeOffset? ValidTo, IReadOnlyList<Rate>? Rates);
    private sealed record ApiResult<T>(bool IsSuccess, T? Value, HttpStatusCode StatusCode);

    private sealed class AggregatedRate(string productName, decimal price)
    {
        public string ProductName { get; } = productName;
        public decimal Price { get; set; } = price;
    }
}
