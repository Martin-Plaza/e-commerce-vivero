using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Carts;

public interface IQuoteCartShippingUseCase
{
    Task<AppResult<IReadOnlyList<ShippingQuoteResponse>>> ExecuteAsync(int userId, CreateShippingQuoteRequest request, CancellationToken cancellationToken = default);
}

public sealed class QuoteCartShippingUseCase : IQuoteCartShippingUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IReadOnlyList<IShippingProvider> _providers;
    private readonly IShippingSettings _settings;
    private readonly TimeProvider _timeProvider;

    public QuoteCartShippingUseCase(
        IApplicationDbContext db,
        IEnumerable<IShippingProvider> providers,
        IShippingSettings settings,
        TimeProvider timeProvider)
    {
        _db = db;
        _providers = providers.Where(x => x.Capabilities.SupportsHomeDelivery).ToList();
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public async Task<AppResult<IReadOnlyList<ShippingQuoteResponse>>> ExecuteAsync(int userId, CreateShippingQuoteRequest request, CancellationToken cancellationToken = default)
    {
        var destinationResult = ShippingQuoteRules.NormalizeAddress(request.Destination);
        if (!destinationResult.IsSuccess)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(destinationResult.Error!.Type, destinationResult.Error.Message, destinationResult.Error.Code);

        var originResult = ShippingQuoteRules.NormalizeAddress(new ShippingAddressRequest(
            _settings.OriginPostalCode,
            _settings.OriginProvince,
            _settings.OriginCity,
            _settings.OriginStreet,
            _settings.OriginStreetNumber));
        if (!originResult.IsSuccess)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(AppErrorType.Unavailable, "El origen de los envíos no está configurado.", "shipping_origin_missing");

        var cart = await _db.Carts.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (cart is null || cart.Items.Count == 0)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(AppErrorType.Validation, "El carrito está vacío.");

        var packageResult = await ShippingQuoteRules.BuildPackagesAsync(_db, cart, cancellationToken);
        if (!packageResult.IsSuccess)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(packageResult.Error!.Type, packageResult.Error.Message, packageResult.Error.Code);

        if (_providers.Count == 0)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(AppErrorType.Unavailable, "No hay proveedores de envío disponibles.", "shipping_provider_unavailable");

        var destination = destinationResult.Value!;
        var providerRequest = new ShippingQuoteRequest(originResult.Value!, destination, packageResult.Value!.Packages, ShippingDeliveryType.HomeDelivery);
        var now = _timeProvider.GetUtcNow();
        var responses = new List<ShippingQuoteResponse>();

        foreach (var provider in _providers)
        {
            IReadOnlyList<ShippingQuote> quotes;
            try
            {
                quotes = await provider.QuoteAsync(providerRequest, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }

            foreach (var quote in quotes.Where(x => x.Price >= 0 && x.ExpiresAt > now))
            {
                var id = Guid.NewGuid();
                var expiresAt = quote.ExpiresAt.UtcDateTime;
                _db.ShippingQuoteReservations.Add(new ShippingQuoteReservation
                {
                    Id = id,
                    UserId = userId,
                    CartId = cart.Id,
                    ProviderCode = provider.Code,
                    ServiceCode = quote.ServiceCode,
                    ServiceName = quote.ServiceName,
                    Price = quote.Price,
                    CartFingerprint = packageResult.Value.CartFingerprint,
                    PostalCode = destination.PostalCode,
                    Province = destination.Province,
                    City = destination.City,
                    Street = destination.Street,
                    StreetNumber = destination.StreetNumber,
                    Floor = destination.Floor,
                    Apartment = destination.Apartment,
                    Notes = destination.Notes,
                    CreatedAtUtc = now.UtcDateTime,
                    ExpiresAtUtc = expiresAt
                });
                responses.Add(new ShippingQuoteResponse(
                    id,
                    provider.Code,
                    quote.ServiceCode,
                    quote.ServiceName,
                    quote.Price,
                    (quote.EstimatedDeliveryFrom ?? now).UtcDateTime,
                    (quote.EstimatedDeliveryTo ?? now).UtcDateTime,
                    expiresAt));
            }
        }

        if (responses.Count == 0)
            return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Failure(AppErrorType.Unavailable, "No pudimos cotizar el envío para esa dirección.", "shipping_quote_unavailable");

        await _db.SaveChangesAsync(cancellationToken);
        return AppResult<IReadOnlyList<ShippingQuoteResponse>>.Success(responses);
    }
}

internal sealed record ShippingPackageBuildResult(IReadOnlyList<ShippingPackage> Packages, string CartFingerprint);

internal static class ShippingQuoteRules
{
    public static AppResult<ShippingAddress> NormalizeAddress(ShippingAddressRequest? value)
    {
        if (value is null)
            return AppResult<ShippingAddress>.Failure(AppErrorType.Validation, "La dirección de envío estructurada es obligatoria.", "shipping_address_required");

        var address = new ShippingAddress(
            value.PostalCode?.Trim().ToUpperInvariant() ?? string.Empty,
            value.Province?.Trim() ?? string.Empty,
            value.City?.Trim() ?? string.Empty,
            value.Street?.Trim() ?? string.Empty,
            value.StreetNumber?.Trim() ?? string.Empty,
            NullIfWhiteSpace(value.Floor),
            NullIfWhiteSpace(value.Apartment),
            NullIfWhiteSpace(value.Notes));

        if (string.IsNullOrWhiteSpace(address.PostalCode) || string.IsNullOrWhiteSpace(address.Province) ||
            string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.StreetNumber))
            return AppResult<ShippingAddress>.Failure(AppErrorType.Validation, "Completá código postal, provincia, ciudad, calle y número.", "shipping_address_incomplete");

        if (address.PostalCode.Length > ValidationLimits.ShippingPostalCode || address.Province.Length > ValidationLimits.ShippingProvince ||
            address.City.Length > ValidationLimits.ShippingCity || address.Street.Length > ValidationLimits.ShippingStreet ||
            address.StreetNumber.Length > ValidationLimits.ShippingStreetNumber || address.Floor?.Length > ValidationLimits.ShippingFloor ||
            address.Apartment?.Length > ValidationLimits.ShippingApartment || address.Notes?.Length > ValidationLimits.ShippingNotes)
            return AppResult<ShippingAddress>.Failure(AppErrorType.Validation, "Uno de los datos de la dirección supera el máximo permitido.", "shipping_address_invalid");

        if (Format(address).Length > ValidationLimits.ShippingAddress)
            return AppResult<ShippingAddress>.Failure(AppErrorType.Validation, "La dirección completa supera el máximo permitido.", "shipping_address_invalid");

        return AppResult<ShippingAddress>.Success(address);
    }

    public static async Task<AppResult<ShippingPackageBuildResult>> BuildPackagesAsync(IApplicationDbContext db, Cart cart, CancellationToken cancellationToken)
    {
        var productIds = cart.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Include(x => x.Variants)
            .Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var packages = new List<ShippingPackage>();
        var fingerprintLines = new List<string>();

        foreach (var item in cart.Items.OrderBy(x => x.ProductId).ThenBy(x => x.ProductVariantId))
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                return AppResult<ShippingPackageBuildResult>.Failure(AppErrorType.Validation, "Uno de los productos del carrito ya no existe.");
            var variant = item.ProductVariantId.HasValue ? product.Variants.SingleOrDefault(x => x.Id == item.ProductVariantId.Value) : null;
            var weight = variant?.PackageWeightGrams ?? product.PackageWeightGrams;
            var length = variant?.PackageLengthCm ?? product.PackageLengthCm;
            var width = variant?.PackageWidthCm ?? product.PackageWidthCm;
            var height = variant?.PackageHeightCm ?? product.PackageHeightCm;
            if (weight is null || length is null || width is null || height is null)
                return AppResult<ShippingPackageBuildResult>.Failure(
                    AppErrorType.Validation,
                    $"Faltan peso o dimensiones de envío para {product.Name}.",
                    "shipping_dimensions_missing");

            var unitPrice = variant?.Price ?? product.Price;
            packages.Add(new ShippingPackage(weight.Value, length.Value, width.Value, height.Value, unitPrice * item.Quantity, item.Quantity));
            fingerprintLines.Add(string.Join('|', product.Id, variant?.Id ?? 0, item.Quantity, weight.Value,
                length.Value.ToString("0.00", CultureInfo.InvariantCulture), width.Value.ToString("0.00", CultureInfo.InvariantCulture),
                height.Value.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        var canonical = string.Join('\n', fingerprintLines);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return AppResult<ShippingPackageBuildResult>.Success(new ShippingPackageBuildResult(packages, fingerprint));
    }

    public static bool Matches(ShippingQuoteReservation quote, ShippingAddress address) =>
        Equal(quote.PostalCode, address.PostalCode) && Equal(quote.Province, address.Province) && Equal(quote.City, address.City) &&
        Equal(quote.Street, address.Street) && Equal(quote.StreetNumber, address.StreetNumber) && Equal(quote.Floor, address.Floor) &&
        Equal(quote.Apartment, address.Apartment) && Equal(quote.Notes, address.Notes);

    public static string Format(ShippingAddress address)
    {
        var unit = string.Join(" ", new[] { address.Floor, address.Apartment }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return $"{address.Street} {address.StreetNumber}{(unit.Length == 0 ? string.Empty : $", {unit}")}, {address.City}, {address.Province}, CP {address.PostalCode}";
    }

    private static bool Equal(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
