using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Stock;
using GymShop.Application.UseCases.Coupons;
using GymShop.Application.UseCases.Payments;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GymShop.Application.UseCases.Carts;

public interface IGetCartUseCase
{
    Task<CartResponse> ExecuteAsync(int userId, CancellationToken cancellationToken = default);
}

public interface IAddCartItemUseCase
{
    Task<AppResult<CartResponse>> ExecuteAsync(int userId, AddCartItemRequest request, CancellationToken cancellationToken = default);
}

public interface IUpdateCartItemUseCase
{
    Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, int? productVariantId, UpdateCartItemRequest request, CancellationToken cancellationToken = default);
}

public interface IRemoveCartItemUseCase
{
    Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, int? productVariantId, CancellationToken cancellationToken = default);
}

public interface IClearCartUseCase
{
    Task<AppResult> ExecuteAsync(int userId, CancellationToken cancellationToken = default);
}

public interface ICheckoutCartUseCase
{
    Task<AppResult<CheckoutResponse>> ExecuteAsync(int userId, CheckoutCartRequest request, CancellationToken cancellationToken = default);
}

public class GetCartUseCase : IGetCartUseCase
{
    private readonly IApplicationDbContext _db;

    public GetCartUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<CartResponse> ExecuteAsync(int userId, CancellationToken cancellationToken = default)
    {
        var cart = await CartQueries.GetOrCreateCartAsync(_db, userId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return await CartQueries.LoadCartResponseAsync(_db, cart.Id, cancellationToken);
    }
}

public class AddCartItemUseCase : IAddCartItemUseCase
{
    private readonly IApplicationDbContext _db;

    public AddCartItemUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<CartResponse>> ExecuteAsync(int userId, AddCartItemRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ProductId <= 0 || request.Quantity <= 0)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.Validation, "Producto y cantidad son obligatorios.");
        }

        var product = await _db.Products.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == request.ProductId, cancellationToken);
        if (product is null || !product.IsActive)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado o inactivo.");
        }

        var cart = await CartQueries.GetOrCreateCartAsync(_db, userId, cancellationToken);
        ProductVariant? variant = null;
        if (product.Variants.Count > 0)
        {
            if (!request.ProductVariantId.HasValue) return AppResult<CartResponse>.Failure(AppErrorType.Validation, "Seleccioná una variante válida.");
            variant = product.Variants.SingleOrDefault(x => x.Id == request.ProductVariantId && x.IsActive);
            if (variant is null) return AppResult<CartResponse>.Failure(AppErrorType.Validation, "La variante seleccionada no existe o no está disponible.");
        }
        else if (request.ProductVariantId.HasValue) return AppResult<CartResponse>.Failure(AppErrorType.Validation, "Este producto no admite variantes.");
        var existingItem = await _db.CartItems.SingleOrDefaultAsync(x => x.CartId == cart.Id && x.ProductId == request.ProductId && x.ProductVariantId == request.ProductVariantId, cancellationToken);
        var newQuantity = (existingItem?.Quantity ?? 0) + request.Quantity;

        if ((variant?.Stock ?? product.Stock) < newQuantity)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.Validation, $"No hay stock suficiente para {product.Name}.");
        }

        if (existingItem is null)
        {
            _db.CartItems.Add(new CartItem
            {
                Cart = cart,
                ProductId = product.Id,
                ProductVariantId = variant?.Id,
                Quantity = request.Quantity
            });
        }
        else
        {
            existingItem.Quantity = newQuantity;
            existingItem.UpdatedAt = DateTime.UtcNow;
        }

        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return AppResult<CartResponse>.Success(await CartQueries.LoadCartResponseAsync(_db, cart.Id, cancellationToken));
    }
}

public class UpdateCartItemUseCase : IUpdateCartItemUseCase
{
    private readonly IApplicationDbContext _db;

    public UpdateCartItemUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, UpdateCartItemRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(userId, productId, null, request, cancellationToken);

    public async Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, int? productVariantId, UpdateCartItemRequest request, CancellationToken cancellationToken = default)
    {
        if (productId <= 0 || request.Quantity <= 0)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.Validation, "Producto y cantidad son obligatorios.");
        }

        var cart = await CartQueries.GetUserCartAsync(_db, userId, cancellationToken);
        if (cart is null)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.NotFound, "Carrito no encontrado.");
        }

        var item = await _db.CartItems.Include(x => x.Product).Include(x => x.ProductVariant).SingleOrDefaultAsync(x => x.CartId == cart.Id && x.ProductId == productId && x.ProductVariantId == productVariantId, cancellationToken);
        if (item is null)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado en el carrito.");
        }

        if (!item.Product.IsActive || (item.ProductVariant is not null && !item.ProductVariant.IsActive) || (item.ProductVariant?.Stock ?? item.Product.Stock) < request.Quantity)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.Validation, $"No hay stock suficiente para {item.Product.Name}.");
        }

        item.Quantity = request.Quantity;
        item.UpdatedAt = DateTime.UtcNow;
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return AppResult<CartResponse>.Success(await CartQueries.LoadCartResponseAsync(_db, cart.Id, cancellationToken));
    }
}

public class RemoveCartItemUseCase : IRemoveCartItemUseCase
{
    private readonly IApplicationDbContext _db;

    public RemoveCartItemUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(userId, productId, null, cancellationToken);

    public async Task<AppResult<CartResponse>> ExecuteAsync(int userId, int productId, int? productVariantId, CancellationToken cancellationToken = default)
    {
        var cart = await CartQueries.GetUserCartAsync(_db, userId, cancellationToken);
        if (cart is null)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.NotFound, "Carrito no encontrado.");
        }

        var item = await _db.CartItems.SingleOrDefaultAsync(x => x.CartId == cart.Id && x.ProductId == productId && x.ProductVariantId == productVariantId, cancellationToken);
        if (item is null)
        {
            return AppResult<CartResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado en el carrito.");
        }

        _db.CartItems.Remove(item);
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return AppResult<CartResponse>.Success(await CartQueries.LoadCartResponseAsync(_db, cart.Id, cancellationToken));
    }
}

public class ClearCartUseCase : IClearCartUseCase
{
    private readonly IApplicationDbContext _db;

    public ClearCartUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult> ExecuteAsync(int userId, CancellationToken cancellationToken = default)
    {
        var cart = await CartQueries.GetUserCartAsync(_db, userId, cancellationToken);
        if (cart is null)
        {
            return AppResult.Success();
        }

        var items = await _db.CartItems.Where(x => x.CartId == cart.Id).ToListAsync(cancellationToken);
        _db.CartItems.RemoveRange(items);
        cart.CouponId = null;
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return AppResult.Success();
    }
}

public class CheckoutCartUseCase : ICheckoutCartUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly ITransactionManager? _transactionManager;
    private readonly IShippingSettings _shippingSettings;
    private readonly IAuditContext? _auditContext;
    private readonly IEnumerable<IPaymentGateway> _gateways;

    public CheckoutCartUseCase(IApplicationDbContext db, ITransactionManager? transactionManager = null, IShippingSettings? shippingSettings = null,
        IAuditContext? auditContext = null, IEnumerable<IPaymentGateway>? gateways = null)
    {
        _db = db;
        _transactionManager = transactionManager;
        _shippingSettings = shippingSettings ?? new FreeShippingSettings();
        _auditContext = auditContext;
        _gateways = gateways ?? [];
    }

    public async Task<AppResult<CheckoutResponse>> ExecuteAsync(int userId, CheckoutCartRequest request, CancellationToken cancellationToken = default)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"server-{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > ValidationLimits.IdempotencyKey)
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia no puede superar 100 caracteres.");
        }

        if (!Enum.TryParse<DeliveryMethod>(request.DeliveryMethod, true, out var deliveryMethod) || !Enum.IsDefined(deliveryMethod))
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La modalidad de entrega no es valida.");
        }

        if (deliveryMethod == DeliveryMethod.HomeDelivery && !request.ShippingQuoteId.HasValue && string.IsNullOrWhiteSpace(request.ShippingAddress))
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La direccion de envio es obligatoria.");
        }

        if (request.ShippingAddress?.Trim().Length > ValidationLimits.ShippingAddress)
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La direccion de envio no puede superar los 300 caracteres.");
        }

        var pickupAddress = _shippingSettings.PickupAddress?.Trim() ?? string.Empty;
        var pickupHours = _shippingSettings.PickupHours?.Trim() ?? string.Empty;
        var pickupInstructions = _shippingSettings.PickupInstructions?.Trim() ?? string.Empty;
        if (deliveryMethod == DeliveryMethod.StorePickup && string.IsNullOrWhiteSpace(pickupAddress))
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "El retiro en tienda no esta disponible porque falta configurar su direccion.", "pickup_configuration_missing");
        }
        if (deliveryMethod == DeliveryMethod.StorePickup &&
            (pickupAddress.Length > ValidationLimits.ShippingAddress ||
             pickupHours.Length > ValidationLimits.PickupHours ||
             pickupInstructions.Length > ValidationLimits.PickupInstructions))
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La configuracion de retiro en tienda supera los limites permitidos.", "pickup_configuration_invalid");
        }

        var requestFingerprint = BuildRequestFingerprint(request, deliveryMethod);
        await using var transaction = await BeginCheckoutTransactionAsync(cancellationToken);

        var idempotentCheckout = await _db.CheckoutSessions
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(
                x => x.UserId == userId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (idempotentCheckout is not null)
        {
            if (!string.Equals(idempotentCheckout.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                return AppResult<CheckoutResponse>.Failure(
                    AppErrorType.Conflict,
                    "La clave de idempotencia ya fue usada con otros datos de checkout.",
                    "checkout_idempotency_conflict");
            }

            return AppResult<CheckoutResponse>.Success(CheckoutSessionMapper.ToResponse(idempotentCheckout));
        }

        var cart = await _db.Carts
            .Include(x => x.Items)
            .Include(x => x.Coupon)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (cart is null || cart.Items.Count == 0)
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "El carrito esta vacio.");
        }

        var productIds = cart.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await _db.Products
            .Include(x => x.Variants).ThenInclude(x => x.Attributes)
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var item in cart.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
            {
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "Uno de los productos del carrito no existe o no esta activo.");
            }

            var variant = item.ProductVariantId.HasValue ? product.Variants.SingleOrDefault(x => x.Id == item.ProductVariantId && x.IsActive) : null;
            if (product.Variants.Count > 0 && variant is null)
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, $"La variante de {product.Name} ya no está disponible.");
            if ((variant?.Stock ?? product.Stock) < item.Quantity)
            {
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, $"No hay stock suficiente para {product.Name}.");
            }
        }

        ShippingQuoteReservation? shippingQuote = null;
        ShippingAddress? shippingDestination = null;
        if (deliveryMethod == DeliveryMethod.HomeDelivery && request.ShippingQuoteId.HasValue)
        {
            var destinationResult = ShippingQuoteRules.NormalizeAddress(request.ShippingDestination);
            if (!destinationResult.IsSuccess)
                return AppResult<CheckoutResponse>.Failure(destinationResult.Error!.Type, destinationResult.Error.Message, destinationResult.Error.Code);
            shippingDestination = destinationResult.Value!;

            shippingQuote = await _db.ShippingQuoteReservations.SingleOrDefaultAsync(
                x => x.Id == request.ShippingQuoteId.Value && x.UserId == userId && x.CartId == cart.Id,
                cancellationToken);
            if (shippingQuote is null)
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "La cotización de envío no existe o no pertenece a este carrito.", "shipping_quote_invalid");
            if (shippingQuote.ExpiresAtUtc <= DateTime.UtcNow)
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Conflict, "La cotización de envío venció. Volvé a cotizar antes de confirmar.", "shipping_quote_expired");
            if (!ShippingQuoteRules.Matches(shippingQuote, shippingDestination))
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Conflict, "La dirección cambió después de cotizar el envío.", "shipping_quote_address_changed");

            var packageResult = await ShippingQuoteRules.BuildPackagesAsync(_db, cart, cancellationToken);
            if (!packageResult.IsSuccess)
                return AppResult<CheckoutResponse>.Failure(packageResult.Error!.Type, packageResult.Error.Message, packageResult.Error.Code);
            if (!string.Equals(packageResult.Value!.CartFingerprint, shippingQuote.CartFingerprint, StringComparison.Ordinal))
                return AppResult<CheckoutResponse>.Failure(AppErrorType.Conflict, "El carrito cambió después de cotizar el envío.", "shipping_quote_cart_changed");
        }

        var orderLines = cart.Items
            .OrderBy(x => x.Id)
            .Select(item =>
            {
                var product = products[item.ProductId];
                var variant = item.ProductVariantId.HasValue ? product.Variants.Single(x => x.Id == item.ProductVariantId) : null;
                var unitPrice = variant?.Price ?? product.Price;

                return new
                {
                    Product = product,
                    Variant = variant,
                    ProductId = product.Id,
                    ProductName = product.Name,
                    UnitPrice = unitPrice,
                    item.Quantity,
                    Subtotal = unitPrice * item.Quantity
                };
            })
            .ToList();

        var checkout = new CheckoutSession
        {
            UserId = userId,
            CartId = cart.Id,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            DeliveryMethod = deliveryMethod,
            ShippingAddress = deliveryMethod == DeliveryMethod.HomeDelivery
                ? shippingDestination is null ? request.ShippingAddress!.Trim() : ShippingQuoteRules.Format(shippingDestination)
                : string.Empty,
            ShippingPostalCode = shippingDestination?.PostalCode,
            ShippingProvince = shippingDestination?.Province,
            ShippingCity = shippingDestination?.City,
            ShippingStreet = shippingDestination?.Street,
            ShippingStreetNumber = shippingDestination?.StreetNumber,
            ShippingFloor = shippingDestination?.Floor,
            ShippingApartment = shippingDestination?.Apartment,
            ShippingNotes = shippingDestination?.Notes,
            ShippingQuoteId = shippingQuote?.Id,
            ShippingProviderCode = shippingQuote?.ProviderCode,
            ShippingServiceCode = shippingQuote?.ServiceCode,
            ShippingServiceName = shippingQuote?.ServiceName,
            PickupAddress = deliveryMethod == DeliveryMethod.StorePickup ? pickupAddress : string.Empty,
            PickupHours = deliveryMethod == DeliveryMethod.StorePickup ? pickupHours : string.Empty,
            PickupInstructions = deliveryMethod == DeliveryMethod.StorePickup ? pickupInstructions : string.Empty,
            Status = CheckoutStatus.AwaitingPayment,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };

        var subtotal = orderLines.Sum(x => x.Subtotal);
        decimal discount = 0;
        if (cart.Coupon is not null)
        {
            var activeUses = await _db.CouponRedemptions.CountAsync(x => x.CouponId == cart.CouponId && x.Status != CouponRedemptionStatus.Released, cancellationToken);
            var userUses = await _db.CouponRedemptions.CountAsync(x => x.CouponId == cart.CouponId && x.UserId == userId && x.Status != CouponRedemptionStatus.Released, cancellationToken);
            var couponError = CouponRules.ValidateAvailability(cart.Coupon, subtotal, DateTime.UtcNow, activeUses, userUses);
            if (couponError is not null) return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, couponError);
            discount = CouponRules.CalculateDiscount(cart.Coupon, subtotal);
            checkout.CouponId = cart.Coupon.Id;
            checkout.CouponCode = cart.Coupon.Code;
        }
        checkout.Subtotal = subtotal;
        checkout.DiscountAmount = discount;
        checkout.ShippingCost = deliveryMethod == DeliveryMethod.HomeDelivery ? shippingQuote?.Price ?? _shippingSettings.HomeDeliveryCost : 0;
        if (checkout.ShippingCost < 0)
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Validation, "El costo de envio configurado no es valido.");
        if (request.ExpectedShippingCost != checkout.ShippingCost ||
            (request.ExpectedSubtotal.HasValue && request.ExpectedSubtotal.Value != subtotal) ||
            (request.ExpectedDiscount.HasValue && request.ExpectedDiscount.Value != discount))
        {
            return AppResult<CheckoutResponse>.Failure(AppErrorType.Conflict, "El precio, descuento o costo de envio cambio. Revisa el resumen antes de confirmar.", "checkout_pricing_changed");
        }
        checkout.Total = Math.Max(0, subtotal - discount) + checkout.ShippingCost;

        foreach (var line in orderLines)
        {
            checkout.Items.Add(new CheckoutItem
            {
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                ProductVariantId = line.Variant?.Id,
                VariantSku = line.Variant?.Sku,
                VariantAttributesJson = line.Variant is null ? null : System.Text.Json.JsonSerializer.Serialize(line.Variant.Attributes.OrderBy(x => x.Name).ToDictionary(x => x.Name, x => x.Value)),
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
                Subtotal = line.Subtotal
            });
        }

        var checkoutCreatedAt = DateTime.UtcNow;
        _db.CartItems.RemoveRange(cart.Items);
        cart.CouponId = null;
        cart.UpdatedAt = checkoutCreatedAt;
        checkout.CartClearedAtUtc = checkoutCreatedAt;
        _db.CheckoutSessions.Add(checkout);
        await _db.SaveChangesAsync(cancellationToken);

        if (checkout.Total == 0)
        {
            var completion = await CheckoutCompletion.CompleteAsync(_db, checkout, null, _auditContext, cancellationToken);
            if (!completion.IsSuccess) return AppResult<CheckoutResponse>.Failure(completion.Error!.Type, completion.Error.Message, completion.Error.Code);
        }

        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        if (checkout.Total > 0 && _gateways.Any())
        {
            var payment = await CheckoutPaymentCreator.CreateAsync(_db, _gateways, checkout,
                new GymShop.Application.DTOs.Payments.CreatePaymentRequest(request.PaymentProvider ?? "BankTransfer", request.PaymentIdempotencyKey),
                PaymentCreationPolicy.Default, cancellationToken);
            if (!payment.IsSuccess && payment.Error?.Code != "payment_creation_failed")
                return AppResult<CheckoutResponse>.Failure(payment.Error!.Type, payment.Error.Message, payment.Error.Code);
        }

        var saved = await CheckoutSessionQueries.LoadAsync(_db, checkout.Id, userId, cancellationToken);
        return AppResult<CheckoutResponse>.Success(CheckoutSessionMapper.ToResponse(saved!));
    }

    private async Task<IApplicationTransaction?> BeginCheckoutTransactionAsync(CancellationToken cancellationToken)
    {
        return _transactionManager is null
            ? null
            : await _transactionManager.BeginCheckoutTransactionAsync(cancellationToken);
    }

    private static string BuildRequestFingerprint(CheckoutCartRequest request, DeliveryMethod deliveryMethod)
    {
        var normalizedAddress = deliveryMethod == DeliveryMethod.HomeDelivery
            ? request.ShippingDestination is null
                ? request.ShippingAddress?.Trim() ?? string.Empty
                : string.Join('|', request.ShippingDestination.PostalCode?.Trim().ToUpperInvariant(), request.ShippingDestination.Province?.Trim(),
                    request.ShippingDestination.City?.Trim(), request.ShippingDestination.Street?.Trim(), request.ShippingDestination.StreetNumber?.Trim(),
                    request.ShippingDestination.Floor?.Trim(), request.ShippingDestination.Apartment?.Trim(), request.ShippingDestination.Notes?.Trim())
            : string.Empty;
        var canonical = string.Join('\n',
            deliveryMethod.ToString(),
            normalizedAddress,
            request.ShippingQuoteId?.ToString("D") ?? "<legacy>",
            request.ExpectedShippingCost.ToString("0.00", CultureInfo.InvariantCulture),
            request.ExpectedSubtotal?.ToString("0.00", CultureInfo.InvariantCulture) ?? "<null>",
            request.ExpectedDiscount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "<null>",
            request.PaymentProvider?.Trim() ?? "BankTransfer");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal static class CartQueries
{
    public static async Task<Cart> GetOrCreateCartAsync(IApplicationDbContext db, int userId, CancellationToken cancellationToken)
    {
        var cart = await GetUserCartAsync(db, userId, cancellationToken);
        if (cart is not null)
        {
            return cart;
        }

        cart = new Cart { UserId = userId };
        db.Carts.Add(cart);
        return cart;
    }

    public static Task<Cart?> GetUserCartAsync(IApplicationDbContext db, int userId, CancellationToken cancellationToken)
    {
        return db.Carts
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    }

    public static async Task<CartResponse> LoadCartResponseAsync(IApplicationDbContext db, int cartId, CancellationToken cancellationToken)
    {
        var cart = await db.Carts
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.ProductVariant!).ThenInclude(x => x.Attributes)
            .Include(x => x.Coupon)
            .SingleAsync(x => x.Id == cartId, cancellationToken);

        var items = cart.Items
            .OrderBy(x => x.Id)
            .Select(x => new CartItemResponse(
                x.ProductId,
                x.Product.Name,
                x.ProductVariant?.Price ?? x.Product.Price,
                x.Quantity,
                (x.ProductVariant?.Price ?? x.Product.Price) * x.Quantity,
                x.ProductVariant?.Stock ?? x.Product.Stock,
                x.Product.ImageUrl,
                x.ProductVariantId,
                x.ProductVariant?.Sku,
                x.ProductVariant?.Attributes.ToDictionary(a => a.Name, a => a.Value)
            ))
            .ToList();

        var subtotal = items.Sum(x => x.Subtotal);
        decimal discount = 0;
        string? couponCode = null;
        if (cart.Coupon is not null)
        {
            var activeUses = await db.CouponRedemptions.CountAsync(x => x.CouponId == cart.CouponId && x.Status != CouponRedemptionStatus.Released, cancellationToken);
            var userUses = await db.CouponRedemptions.CountAsync(x => x.CouponId == cart.CouponId && x.UserId == cart.UserId && x.Status != CouponRedemptionStatus.Released, cancellationToken);
            var error = CouponRules.ValidateAvailability(cart.Coupon, subtotal, DateTime.UtcNow, activeUses, userUses);
            if (error is null)
            {
                discount = CouponRules.CalculateDiscount(cart.Coupon, subtotal);
                couponCode = cart.Coupon.Code;
            }
            else
            {
                cart.CouponId = null;
                cart.Coupon = null;
                cart.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        return new CartResponse(cart.Id, cart.UserId, subtotal, discount, Math.Max(0, subtotal - discount), couponCode, items);
    }
}
