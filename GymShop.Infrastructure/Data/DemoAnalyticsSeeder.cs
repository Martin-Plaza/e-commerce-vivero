using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GymShop.Infrastructure.Data;

internal static class DemoAnalyticsSeeder
{
    private const string MarkerPrefix = "demo-metrics-";

    private sealed record OrderSeed(
        int DaysAgo,
        OrderStatus Status,
        int ProductIndex,
        int Quantity,
        decimal Shipping,
        string? CouponCode = null,
        decimal Discount = 0);

    private static readonly OrderSeed[] Orders =
    [
        new(29, OrderStatus.Delivered, 0, 1, 6500),
        new(27, OrderStatus.Delivered, 3, 2, 6500, "BIENVENIDA10", 2560),
        new(25, OrderStatus.Delivered, 5, 2, 6500),
        new(23, OrderStatus.Canceled, 6, 1, 6500),
        new(21, OrderStatus.Delivered, 4, 2, 0, "VERDE15", 6750),
        new(19, OrderStatus.Delivered, 0, 1, 6500),
        new(17, OrderStatus.Refunded, 2, 2, 6500),
        new(15, OrderStatus.Delivered, 7, 3, 6500, "BIENVENIDA10", 4950),
        new(13, OrderStatus.Shipped, 3, 3, 6500),
        new(11, OrderStatus.Delivered, 5, 4, 6500),
        new(9, OrderStatus.Canceled, 4, 1, 6500),
        new(8, OrderStatus.Delivered, 6, 2, 0, "VERDE15", 8670),
        new(7, OrderStatus.Delivered, 2, 1, 6500),
        new(6, OrderStatus.Paid, 0, 2, 6500),
        new(5, OrderStatus.Delivered, 3, 2, 6500, "BIENVENIDA10", 2560),
        new(4, OrderStatus.Preparing, 7, 2, 6500),
        new(3, OrderStatus.Canceled, 5, 1, 6500),
        new(2, OrderStatus.Paid, 4, 2, 0),
        new(1, OrderStatus.Preparing, 6, 1, 6500),
        new(0, OrderStatus.Pending, 2, 1, 6500),
    ];

    public static async Task SeedAsync(GymShopDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("DemoAnalytics:Enabled")) return;

        var products = await db.Products
            .Where(product => product.IsActive)
            .OrderBy(product => product.Id)
            .ToListAsync(cancellationToken);
        if (products.Count < 8) return;

        var customerRole = await db.Roles.SingleAsync(role => role.Name == "Customer", cancellationToken);
        var customer = await db.Users.SingleOrDefaultAsync(user => user.Email == "cliente.demo@raizviva.demo", cancellationToken);
        if (customer is null)
        {
            customer = new User
            {
                Email = "cliente.demo@raizviva.demo",
                Name = "Cliente",
                LastName = "Demostración",
                PasswordHash = "DEMO_ACCOUNT_LOGIN_DISABLED",
                RoleId = customerRole.Id,
                IsActive = false,
                EmailVerifiedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow.AddDays(-90)
            };
            db.Users.Add(customer);
            await db.SaveChangesAsync(cancellationToken);
        }

        var coupons = await EnsureCouponsAsync(db, cancellationToken);
        var now = DateTime.UtcNow;

        for (var index = 0; index < Orders.Length; index++)
        {
            var marker = $"{MarkerPrefix}{index + 1:D2}";
            if (await db.Orders.AnyAsync(order => order.CheckoutIdempotencyKey == marker, cancellationToken)) continue;

            var seed = Orders[index];
            var product = products[seed.ProductIndex];
            var createdAt = now.Date.AddDays(-seed.DaysAgo).AddHours(13 + index % 7);
            var subtotal = product.Price * seed.Quantity;
            var total = subtotal - seed.Discount + seed.Shipping;
            var order = new Order
            {
                UserId = customer.Id,
                CheckoutIdempotencyKey = marker,
                CheckoutRequestFingerprint = marker,
                CreatedAt = createdAt,
                UpdatedAt = createdAt.AddHours(2),
                Status = seed.Status,
                Subtotal = subtotal,
                CouponCode = seed.CouponCode,
                DiscountAmount = seed.Discount,
                ShippingCost = seed.Shipping,
                Total = total,
                DeliveryMethod = seed.Shipping == 0 ? DeliveryMethod.StorePickup : DeliveryMethod.HomeDelivery,
                ShippingAddress = seed.Shipping == 0 ? string.Empty : "Av. Siempre Verde 742, CABA",
                ShippingPostalCode = seed.Shipping == 0 ? null : "1425",
                ShippingProvince = seed.Shipping == 0 ? null : "Buenos Aires",
                ShippingCity = seed.Shipping == 0 ? null : "CABA",
                ShippingStreet = seed.Shipping == 0 ? null : "Av. Siempre Verde",
                ShippingStreetNumber = seed.Shipping == 0 ? null : "742",
                PickupAddress = seed.Shipping == 0 ? "Vivero Raíz Viva" : string.Empty,
                PickupHours = seed.Shipping == 0 ? "Lunes a sábado de 9 a 18 h" : string.Empty,
                PickupInstructions = string.Empty,
                CancellationReason = seed.Status == OrderStatus.Canceled ? "Pedido de demostración cancelado por el cliente." : null
            };
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = seed.Quantity,
                Subtotal = subtotal
            });

            var paymentStatus = seed.Status switch
            {
                OrderStatus.Pending => PaymentStatus.Pending,
                OrderStatus.Canceled => PaymentStatus.Canceled,
                OrderStatus.Refunded => PaymentStatus.Refunded,
                _ => PaymentStatus.Approved
            };
            order.Payments.Add(new Payment
            {
                Provider = index % 3 == 0 ? "BankTransfer" : "MercadoPago",
                ExternalReference = $"DEMO-{index + 1:D4}",
                IdempotencyKey = $"{marker}-payment",
                Amount = total,
                Currency = "ARS",
                Status = paymentStatus,
                CreatedAt = createdAt.AddMinutes(5),
                UpdatedAt = createdAt.AddHours(1),
                PaidAt = paymentStatus is PaymentStatus.Approved or PaymentStatus.Refunded ? createdAt.AddHours(1) : null
            });

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            if (seed.CouponCode is not null && seed.Status is not OrderStatus.Canceled and not OrderStatus.Refunded)
            {
                db.CouponRedemptions.Add(new CouponRedemption
                {
                    CouponId = coupons[seed.CouponCode].Id,
                    UserId = customer.Id,
                    OrderId = order.Id,
                    Status = CouponRedemptionStatus.Consumed,
                    ReservedAtUtc = createdAt,
                    ConsumedAtUtc = createdAt.AddHours(1)
                });
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static async Task<Dictionary<string, Coupon>> EnsureCouponsAsync(GymShopDbContext db, CancellationToken cancellationToken)
    {
        var definitions = new[]
        {
            new Coupon { Code = "BIENVENIDA10", Name = "Bienvenida 10%", Type = CouponType.Percentage, Value = 10, MaximumDiscount = 8000, IsActive = true },
            new Coupon { Code = "VERDE15", Name = "Semana verde 15%", Type = CouponType.Percentage, Value = 15, MinimumPurchase = 30000, MaximumDiscount = 12000, IsActive = true },
            new Coupon { Code = "PRIMAVERA5000", Name = "Primavera $5.000", Type = CouponType.FixedAmount, Value = 5000, MinimumPurchase = 35000, IsActive = false }
        };

        foreach (var definition in definitions)
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(item => item.Code == definition.Code, cancellationToken);
            if (coupon is null)
            {
                definition.CreatedAtUtc = DateTime.UtcNow.AddDays(-60);
                db.Coupons.Add(definition);
            }
            else
            {
                coupon.Name = definition.Name;
                coupon.Type = definition.Type;
                coupon.Value = definition.Value;
                coupon.MinimumPurchase = definition.MinimumPurchase;
                coupon.MaximumDiscount = definition.MaximumDiscount;
                coupon.IsActive = definition.IsActive;
                coupon.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await db.Coupons.Where(coupon => definitions.Select(item => item.Code).Contains(coupon.Code))
            .ToDictionaryAsync(coupon => coupon.Code, cancellationToken);
    }
}
