using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.UseCases.Coupons;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Payments;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Carts;

public interface IGetCheckoutSessionUseCase
{
    Task<AppResult<CheckoutResponse>> ExecuteAsync(int id, int userId, CancellationToken cancellationToken = default);
}

public sealed class GetCheckoutSessionUseCase : IGetCheckoutSessionUseCase
{
    private readonly IApplicationDbContext _db;
    public GetCheckoutSessionUseCase(IApplicationDbContext db) => _db = db;

    public async Task<AppResult<CheckoutResponse>> ExecuteAsync(int id, int userId, CancellationToken cancellationToken = default)
    {
        var checkout = await CheckoutSessionQueries.LoadAsync(_db, id, userId, cancellationToken);
        return checkout is null
            ? AppResult<CheckoutResponse>.Failure(AppErrorType.NotFound, "Checkout no encontrado.")
            : AppResult<CheckoutResponse>.Success(CheckoutSessionMapper.ToResponse(checkout));
    }
}

internal static class CheckoutSessionQueries
{
    public static Task<CheckoutSession?> LoadAsync(IApplicationDbContext db, int id, int? userId, CancellationToken cancellationToken) =>
        db.CheckoutSessions
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Order)
            .SingleOrDefaultAsync(x => x.Id == id && (!userId.HasValue || x.UserId == userId.Value), cancellationToken);
}

internal static class CheckoutSessionMapper
{
    public static CheckoutResponse ToResponse(CheckoutSession checkout)
    {
        var destination = checkout.DeliveryMethod == DeliveryMethod.HomeDelivery && checkout.ShippingPostalCode is not null
            ? new OrderShippingAddressResponse(checkout.ShippingPostalCode, checkout.ShippingProvince ?? string.Empty,
                checkout.ShippingCity ?? string.Empty, checkout.ShippingStreet ?? string.Empty,
                checkout.ShippingStreetNumber ?? string.Empty, checkout.ShippingFloor, checkout.ShippingApartment, checkout.ShippingNotes)
            : null;
        return new CheckoutResponse(
            checkout.Id,
            checkout.OrderId,
            checkout.UserId,
            checkout.CreatedAtUtc,
            checkout.ExpiresAtUtc,
            checkout.Subtotal,
            checkout.CouponCode,
            checkout.DiscountAmount,
            checkout.DeliveryMethod.ToString(),
            checkout.ShippingCost,
            checkout.Total,
            checkout.Status.ToString(),
            checkout.ShippingAddress,
            checkout.PickupAddress,
            checkout.PickupHours,
            checkout.PickupInstructions,
            checkout.Items.OrderBy(x => x.Id).Select(x => new OrderItemResponse(
                x.ProductId, x.ProductName, x.UnitPrice, x.Quantity, x.Subtotal, x.ProductVariantId, x.VariantSku,
                string.IsNullOrWhiteSpace(x.VariantAttributesJson)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(x.VariantAttributesJson))).ToList(),
            checkout.Payments.OrderByDescending(x => x.Id).Select(PaymentMapper.ToResponse).FirstOrDefault(),
            destination,
            checkout.ShippingProviderCode,
            checkout.ShippingServiceCode,
            checkout.ShippingServiceName);
    }
}

internal static class CheckoutCompletion
{
    public static async Task<AppResult<Order>> CompleteAsync(
        IApplicationDbContext db,
        CheckoutSession checkout,
        Payment? payment,
        IAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        if (checkout.OrderId.HasValue)
        {
            var existing = await db.Orders.Include(x => x.Items).SingleAsync(x => x.Id == checkout.OrderId.Value, cancellationToken);
            return AppResult<Order>.Success(existing);
        }

        if (checkout.CouponId.HasValue && checkout.UserId.HasValue)
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Id == checkout.CouponId.Value, cancellationToken);
            var activeUses = await db.CouponRedemptions.CountAsync(
                x => x.CouponId == checkout.CouponId.Value && x.Status != CouponRedemptionStatus.Released,
                cancellationToken);
            var userUses = await db.CouponRedemptions.CountAsync(
                x => x.CouponId == checkout.CouponId.Value && x.UserId == checkout.UserId && x.Status != CouponRedemptionStatus.Released,
                cancellationToken);
            var couponError = coupon is null
                ? "El cupón aplicado ya no existe."
                : CouponRules.ValidateAvailability(coupon, checkout.Subtotal, DateTime.UtcNow, activeUses, userUses);
            if (couponError is not null)
                return await MarkStockFailureAsync(db, checkout, payment, couponError, cancellationToken);
        }

        var productIds = checkout.Items.Select(x => x.ProductId).Distinct().ToList();
        var products = await db.Products.Include(x => x.Variants)
            .Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        foreach (var line in checkout.Items)
        {
            if (!products.TryGetValue(line.ProductId, out var product) || !product.IsActive)
                return await MarkStockFailureAsync(db, checkout, payment, "Uno de los productos pagados ya no está disponible.", cancellationToken);
            var variant = line.ProductVariantId.HasValue
                ? product.Variants.SingleOrDefault(x => x.Id == line.ProductVariantId.Value && x.IsActive)
                : null;
            if (line.ProductVariantId.HasValue && variant is null)
                return await MarkStockFailureAsync(db, checkout, payment, $"La variante de {line.ProductName} ya no está disponible.", cancellationToken);
            if ((variant?.Stock ?? product.Stock) < line.Quantity)
                return await MarkStockFailureAsync(db, checkout, payment, $"No queda stock suficiente de {line.ProductName}.", cancellationToken);
        }

        var now = DateTime.UtcNow;
        var order = new Order
        {
            UserId = checkout.UserId,
            CheckoutIdempotencyKey = checkout.IdempotencyKey,
            CheckoutRequestFingerprint = checkout.RequestFingerprint,
            CreatedAt = now,
            UpdatedAt = now,
            Status = OrderStatus.Paid,
            Subtotal = checkout.Subtotal,
            CouponCode = checkout.CouponCode,
            DiscountAmount = checkout.DiscountAmount,
            DeliveryMethod = checkout.DeliveryMethod,
            ShippingCost = checkout.ShippingCost,
            Total = checkout.Total,
            ShippingAddress = checkout.ShippingAddress,
            ShippingPostalCode = checkout.ShippingPostalCode,
            ShippingProvince = checkout.ShippingProvince,
            ShippingCity = checkout.ShippingCity,
            ShippingStreet = checkout.ShippingStreet,
            ShippingStreetNumber = checkout.ShippingStreetNumber,
            ShippingFloor = checkout.ShippingFloor,
            ShippingApartment = checkout.ShippingApartment,
            ShippingNotes = checkout.ShippingNotes,
            ShippingQuoteId = checkout.ShippingQuoteId,
            ShippingProviderCode = checkout.ShippingProviderCode,
            ShippingServiceCode = checkout.ShippingServiceCode,
            ShippingServiceName = checkout.ShippingServiceName,
            PickupAddress = checkout.PickupAddress,
            PickupHours = checkout.PickupHours,
            PickupInstructions = checkout.PickupInstructions
        };
        order.GuestFirstName = checkout.GuestFirstName;
        order.GuestLastName = checkout.GuestLastName;
        order.GuestEmail = checkout.GuestEmail;
        order.GuestPhone = checkout.GuestPhone;
        order.GuestAccessToken = checkout.GuestAccessToken;

        foreach (var line in checkout.Items)
        {
            var product = products[line.ProductId];
            var variant = line.ProductVariantId.HasValue ? product.Variants.Single(x => x.Id == line.ProductVariantId.Value) : null;
            order.Items.Add(new OrderItem
            {
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                ProductVariantId = line.ProductVariantId,
                VariantSku = line.VariantSku,
                VariantAttributesJson = line.VariantAttributesJson,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
                Subtotal = line.Subtotal
            });
            var previousStock = variant?.Stock ?? product.Stock;
            if (variant is null) product.Stock -= line.Quantity;
            else variant.Stock -= line.Quantity;
            product.UpdatedAt = now;
            StockMovementRecorder.Add(db, product, StockMovementType.Sale, -line.Quantity, previousStock,
                "Venta confirmada después de la acreditación del pago.", order: order, variant: variant);
        }

        if (checkout.CouponId.HasValue && checkout.UserId.HasValue)
        {
            order.CouponRedemption = new CouponRedemption
            {
                CouponId = checkout.CouponId.Value,
                UserId = checkout.UserId.Value,
                Status = CouponRedemptionStatus.Consumed,
                ReservedAtUtc = now,
                ConsumedAtUtc = now
            };
        }

        db.Orders.Add(order);
        checkout.Order = order;
        checkout.Status = CheckoutStatus.Completed;
        checkout.CompletedAtUtc = now;
        if (payment is not null)
        {
            payment.Order = order;
            payment.Status = PaymentStatus.Approved;
            payment.PaidAt ??= now;
            payment.UpdatedAt = now;
            payment.FailureReason = null;
        }

        // Checkouts created before the cart-detachment rollout still need the legacy cleanup.
        // New checkouts clear the purchased snapshot up front, so touching the cart here could
        // remove products the customer added for a later purchase while payment was pending.
        if (checkout.CartClearedAtUtc is null && checkout.CartId.HasValue)
        {
            var cartItems = await db.CartItems.Where(x => x.CartId == checkout.CartId.Value).ToListAsync(cancellationToken);
            foreach (var purchased in checkout.Items)
            {
                var current = cartItems.SingleOrDefault(x => x.ProductId == purchased.ProductId && x.ProductVariantId == purchased.ProductVariantId);
                if (current is null) continue;
                if (current.Quantity <= purchased.Quantity) db.CartItems.Remove(current);
                else current.Quantity -= purchased.Quantity;
            }
            var cart = await db.Carts.SingleAsync(x => x.Id == checkout.CartId.Value, cancellationToken);
            if (cart.CouponId == checkout.CouponId) cart.CouponId = null;
            cart.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        AuditTrail.Add(db, auditContext,
            payment is null ? "FreeOrderConfirmed" : "OrderCreatedAfterPayment",
            "Order", order.Id, null,
            new { status = order.Status.ToString(), checkoutSessionId = checkout.Id, paymentId = payment?.Id },
            payment is null ? "Pedido confirmado sin pago externo." : "Pedido creado después de acreditar el pago.");
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<Order>.Success(order);
    }

    private static async Task<AppResult<Order>> MarkStockFailureAsync(
        IApplicationDbContext db, CheckoutSession checkout, Payment? payment, string detail, CancellationToken cancellationToken)
    {
        checkout.Status = CheckoutStatus.StockUnavailable;
        checkout.CompletedAtUtc = DateTime.UtcNow;
        if (payment is not null)
        {
            payment.Status = PaymentStatus.Approved;
            payment.PaidAt ??= DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            payment.FailureReason = $"{detail} Te contactaremos para ofrecerte un cambio o gestionar la devolución.";
        }
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<Order>.Failure(AppErrorType.Conflict, detail, "paid_checkout_stock_unavailable");
    }
}
