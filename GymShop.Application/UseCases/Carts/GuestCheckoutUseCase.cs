using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Payments;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Carts;

public interface IGuestCheckoutUseCase
{
    Task<AppResult<GuestCheckoutResponse>> ExecuteAsync(GuestCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<AppResult<OrderResponse>> GetOrderAsync(int id, Guid accessToken, CancellationToken cancellationToken = default);
    Task<AppResult<CheckoutResponse>> GetCheckoutAsync(int id, Guid accessToken, CancellationToken cancellationToken = default);
}

public sealed class GuestCheckoutUseCase(
    IApplicationDbContext db,
    IShippingSettings shipping,
    IBankTransferSettings bankTransfer,
    IEnumerable<IPaymentGateway> gateways,
    ITransactionManager? transactionManager = null) : IGuestCheckoutUseCase
{
    public async Task<AppResult<GuestCheckoutResponse>> ExecuteAsync(GuestCheckoutRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<DeliveryMethod>(request.DeliveryMethod, true, out var deliveryMethod) || !Enum.IsDefined(deliveryMethod))
            return Failure("La modalidad de entrega no es válida.");
        if (!string.Equals(request.PaymentProvider, "BankTransfer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.PaymentProvider, "MercadoPago", StringComparison.OrdinalIgnoreCase))
            return Failure("El medio de pago no es válido.");
        if (deliveryMethod == DeliveryMethod.HomeDelivery && request.ShippingDestination is null)
            return Failure("La dirección de envío es obligatoria.");
        if (deliveryMethod == DeliveryMethod.StorePickup && string.IsNullOrWhiteSpace(shipping.PickupAddress))
            return Failure("El retiro en tienda no está disponible.");

        var customer = new GuestCustomer(
            request.Customer.FirstName.Trim(), request.Customer.LastName.Trim(),
            request.Customer.Email.Trim().ToLowerInvariant(), request.Customer.Phone.Trim());
        if (customer.FirstName.Length == 0 || customer.LastName.Length == 0 || customer.Email.Length == 0 || customer.Phone.Length == 0)
            return Failure("Completá nombre, apellido, email y teléfono.");

        var normalizedItems = request.Items
            .GroupBy(x => new { x.ProductId, x.ProductVariantId })
            .Select(x => new GuestCartItemRequest(x.Key.ProductId, x.Sum(item => item.Quantity), x.Key.ProductVariantId))
            .ToList();
        if (normalizedItems.Count == 0 || normalizedItems.Any(x => x.Quantity is < 1 or > 100))
            return Failure("El carrito no es válido.");

        var idempotencyKey = request.IdempotencyKey.Trim();
        if (idempotencyKey.Length == 0) return Failure("La clave de confirmación no es válida.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)))).ToLowerInvariant();
        var existingOrder = await db.Orders.Include(x => x.User).Include(x => x.Items).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.UserId == null && x.CheckoutIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingOrder is not null && existingOrder.GuestAccessToken.HasValue)
        {
            if (!string.Equals(existingOrder.CheckoutRequestFingerprint, fingerprint, StringComparison.Ordinal))
                return AppResult<GuestCheckoutResponse>.Failure(AppErrorType.Conflict, "La clave de confirmación ya fue utilizada con otros datos.", "idempotency_key_reused");
            return AppResult<GuestCheckoutResponse>.Success(new GuestCheckoutResponse(
                "Order", existingOrder.GuestAccessToken.Value, existingOrder.ExpiresAtUtc ?? existingOrder.CreatedAt,
                OrderMapper.ToResponse(existingOrder), null));
        }

        var existingCheckout = await db.CheckoutSessions.Include(x => x.Items).Include(x => x.Payments).Include(x => x.Order)
            .SingleOrDefaultAsync(x => x.UserId == null && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingCheckout is not null && existingCheckout.GuestAccessToken.HasValue)
        {
            if (!string.Equals(existingCheckout.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                return AppResult<GuestCheckoutResponse>.Failure(AppErrorType.Conflict, "La clave de confirmación ya fue utilizada con otros datos.", "idempotency_key_reused");
            return AppResult<GuestCheckoutResponse>.Success(new GuestCheckoutResponse(
                "Checkout", existingCheckout.GuestAccessToken.Value, existingCheckout.ExpiresAtUtc,
                null, CheckoutSessionMapper.ToResponse(existingCheckout)));
        }

        var productIds = normalizedItems.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products.Include(x => x.Variants).ThenInclude(x => x.Attributes)
            .Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var lines = new List<GuestLine>();
        foreach (var item in normalizedItems)
        {
            if (!products.TryGetValue(item.ProductId, out var product) || !product.IsActive)
                return Failure("Uno de los productos ya no está disponible.");
            var variant = item.ProductVariantId.HasValue
                ? product.Variants.SingleOrDefault(x => x.Id == item.ProductVariantId.Value && x.IsActive)
                : null;
            if (product.Variants.Count > 0 && variant is null)
                return Failure($"La variante de {product.Name} ya no está disponible.");
            if ((variant?.Stock ?? product.Stock) < item.Quantity)
                return Failure($"No hay stock suficiente para {product.Name}.");
            var price = variant?.Price ?? product.Price;
            lines.Add(new GuestLine(product, variant, item.Quantity, price));
        }

        var subtotal = lines.Sum(x => x.UnitPrice * x.Quantity);
        var shippingCost = deliveryMethod == DeliveryMethod.HomeDelivery ? shipping.HomeDeliveryCost : 0;
        if (subtotal != request.ExpectedSubtotal || shippingCost != request.ExpectedShippingCost)
            return AppResult<GuestCheckoutResponse>.Failure(AppErrorType.Conflict,
                "El precio o el costo de envío cambió. Revisá el resumen antes de confirmar.", "checkout_pricing_changed");

        var destination = deliveryMethod == DeliveryMethod.HomeDelivery ? NormalizeAddress(request.ShippingDestination!) : null;
        if (deliveryMethod == DeliveryMethod.HomeDelivery && destination is null)
            return Failure("Completá todos los datos obligatorios de la dirección.");

        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(bankTransfer.PendingOrderLifetimeHours);
        var accessToken = Guid.NewGuid();
        await using var transaction = transactionManager is null ? null : await transactionManager.BeginCheckoutTransactionAsync(cancellationToken);

        if (string.Equals(request.PaymentProvider, "BankTransfer", StringComparison.OrdinalIgnoreCase))
        {
            var order = BuildOrder(customer, accessToken, deliveryMethod, destination, lines, subtotal, shippingCost, now, expiresAt, idempotencyKey, fingerprint);
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            order.Payments.Add(new Payment
            {
                Provider = "BankTransfer", ExternalReference = await BankTransferReference.CreateUniqueAsync(db, cancellationToken), ProviderPreferenceId = $"transfer-{order.Id}",
                IdempotencyKey = $"guest-transfer-{idempotencyKey}", Amount = order.Total, Currency = "ARS",
                Status = PaymentStatus.Pending, CreatedAt = now, UpdatedAt = now
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AppResult<GuestCheckoutResponse>.Success(new GuestCheckoutResponse(
                "Order", accessToken, expiresAt, OrderMapper.ToResponse(order), null));
        }

        var checkout = BuildCheckout(customer, accessToken, deliveryMethod, destination, lines, subtotal, shippingCost, now, expiresAt, idempotencyKey, fingerprint);
        db.CheckoutSessions.Add(checkout);
        await db.SaveChangesAsync(cancellationToken);
        var payment = await CheckoutPaymentCreator.CreateAsync(db, gateways, checkout,
            new CreatePaymentRequest("MercadoPago", $"guest-mp-{idempotencyKey}"), PaymentCreationPolicy.Default, cancellationToken);
        if (!payment.IsSuccess) return AppResult<GuestCheckoutResponse>.Failure(payment.Error!.Type, payment.Error.Message, payment.Error.Code);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return AppResult<GuestCheckoutResponse>.Success(new GuestCheckoutResponse(
            "Checkout", accessToken, expiresAt, null, CheckoutSessionMapper.ToResponse(checkout)));
    }

    public async Task<AppResult<OrderResponse>> GetOrderAsync(int id, Guid accessToken, CancellationToken cancellationToken = default)
    {
        var order = await db.Orders.AsNoTracking().Include(x => x.User).Include(x => x.Items).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == null && x.GuestAccessToken == accessToken, cancellationToken);
        return order is null
            ? AppResult<OrderResponse>.Failure(AppErrorType.NotFound, "Orden no encontrada.")
            : AppResult<OrderResponse>.Success(OrderMapper.ToResponse(order));
    }

    public async Task<AppResult<CheckoutResponse>> GetCheckoutAsync(int id, Guid accessToken, CancellationToken cancellationToken = default)
    {
        var checkout = await db.CheckoutSessions.AsNoTracking().Include(x => x.Items).Include(x => x.Payments).Include(x => x.Order)
            .SingleOrDefaultAsync(x => x.Id == id && x.UserId == null && x.GuestAccessToken == accessToken, cancellationToken);
        return checkout is null
            ? AppResult<CheckoutResponse>.Failure(AppErrorType.NotFound, "Checkout no encontrado.")
            : AppResult<CheckoutResponse>.Success(CheckoutSessionMapper.ToResponse(checkout));
    }

    private Order BuildOrder(GuestCustomer customer, Guid token, DeliveryMethod delivery, ShippingAddress? address,
        IEnumerable<GuestLine> lines, decimal subtotal, decimal shippingCost, DateTime now, DateTime expiresAt, string key, string fingerprint)
    {
        var order = new Order
        {
            GuestFirstName = customer.FirstName, GuestLastName = customer.LastName, GuestEmail = customer.Email, GuestPhone = customer.Phone,
            GuestAccessToken = token, ExpiresAtUtc = expiresAt, StockReserved = false,
            CheckoutIdempotencyKey = key, CheckoutRequestFingerprint = fingerprint, CreatedAt = now, UpdatedAt = now,
            Status = OrderStatus.Pending, Subtotal = subtotal, ShippingCost = shippingCost, Total = subtotal + shippingCost,
            DeliveryMethod = delivery, ShippingAddress = address is null ? string.Empty : FormatAddress(address),
            ShippingPostalCode = address?.PostalCode, ShippingProvince = address?.Province, ShippingCity = address?.City,
            ShippingStreet = address?.Street, ShippingStreetNumber = address?.StreetNumber, ShippingFloor = address?.Floor,
            ShippingApartment = address?.Apartment, ShippingNotes = address?.Notes,
            PickupAddress = delivery == DeliveryMethod.StorePickup ? shipping.PickupAddress.Trim() : string.Empty,
            PickupHours = delivery == DeliveryMethod.StorePickup ? shipping.PickupHours.Trim() : string.Empty,
            PickupInstructions = delivery == DeliveryMethod.StorePickup ? shipping.PickupInstructions.Trim() : string.Empty,
        };
        AddOrderLines(order, lines);
        return order;
    }

    private CheckoutSession BuildCheckout(GuestCustomer customer, Guid token, DeliveryMethod delivery, ShippingAddress? address,
        IEnumerable<GuestLine> lines, decimal subtotal, decimal shippingCost, DateTime now, DateTime expiresAt, string key, string fingerprint)
    {
        var checkout = new CheckoutSession
        {
            GuestFirstName = customer.FirstName, GuestLastName = customer.LastName, GuestEmail = customer.Email, GuestPhone = customer.Phone,
            GuestAccessToken = token, IdempotencyKey = key, RequestFingerprint = fingerprint, Status = CheckoutStatus.AwaitingPayment,
            CreatedAtUtc = now, ExpiresAtUtc = expiresAt, Subtotal = subtotal, ShippingCost = shippingCost, Total = subtotal + shippingCost,
            DeliveryMethod = delivery, ShippingAddress = address is null ? string.Empty : FormatAddress(address),
            ShippingPostalCode = address?.PostalCode, ShippingProvince = address?.Province, ShippingCity = address?.City,
            ShippingStreet = address?.Street, ShippingStreetNumber = address?.StreetNumber, ShippingFloor = address?.Floor,
            ShippingApartment = address?.Apartment, ShippingNotes = address?.Notes,
            PickupAddress = delivery == DeliveryMethod.StorePickup ? shipping.PickupAddress.Trim() : string.Empty,
            PickupHours = delivery == DeliveryMethod.StorePickup ? shipping.PickupHours.Trim() : string.Empty,
            PickupInstructions = delivery == DeliveryMethod.StorePickup ? shipping.PickupInstructions.Trim() : string.Empty
        };
        foreach (var line in lines) checkout.Items.Add(new CheckoutItem
        {
            ProductId = line.Product.Id, ProductName = line.Product.Name, ProductVariantId = line.Variant?.Id,
            VariantSku = line.Variant?.Sku,
            VariantAttributesJson = line.Variant is null ? null : JsonSerializer.Serialize(line.Variant.Attributes.OrderBy(x => x.Name).ToDictionary(x => x.Name, x => x.Value)),
            UnitPrice = line.UnitPrice, Quantity = line.Quantity, Subtotal = line.UnitPrice * line.Quantity
        });
        return checkout;
    }

    private void AddOrderLines(Order order, IEnumerable<GuestLine> lines)
    {
        foreach (var line in lines) order.Items.Add(new OrderItem
        {
            ProductId = line.Product.Id, ProductName = line.Product.Name, ProductVariantId = line.Variant?.Id,
            VariantSku = line.Variant?.Sku,
            VariantAttributesJson = line.Variant is null ? null : JsonSerializer.Serialize(line.Variant.Attributes.OrderBy(x => x.Name).ToDictionary(x => x.Name, x => x.Value)),
            UnitPrice = line.UnitPrice, Quantity = line.Quantity, Subtotal = line.UnitPrice * line.Quantity
        });
        if (order.DeliveryMethod == DeliveryMethod.StorePickup)
        {
            order.PickupAddress = shipping.PickupAddress.Trim(); order.PickupHours = shipping.PickupHours.Trim(); order.PickupInstructions = shipping.PickupInstructions.Trim();
        }
    }

    private static ShippingAddress? NormalizeAddress(ShippingAddressRequest request)
    {
        var values = new[] { request.PostalCode, request.Province, request.City, request.Street, request.StreetNumber };
        if (values.Any(string.IsNullOrWhiteSpace)) return null;
        return new ShippingAddress(request.PostalCode.Trim(), request.Province.Trim(), request.City.Trim(), request.Street.Trim(), request.StreetNumber.Trim(), request.Floor?.Trim(), request.Apartment?.Trim(), request.Notes?.Trim());
    }

    private static string FormatAddress(ShippingAddress value) =>
        $"{value.Street} {value.StreetNumber}{(string.IsNullOrWhiteSpace(value.Floor) ? string.Empty : $", piso {value.Floor}")}{(string.IsNullOrWhiteSpace(value.Apartment) ? string.Empty : $", depto. {value.Apartment}")}, {value.City}, {value.Province} ({value.PostalCode})";

    private static AppResult<GuestCheckoutResponse> Failure(string message) => AppResult<GuestCheckoutResponse>.Failure(AppErrorType.Validation, message);
    private sealed record GuestCustomer(string FirstName, string LastName, string Email, string Phone);
    private sealed record GuestLine(Product Product, ProductVariant? Variant, int Quantity, decimal UnitPrice);
}
