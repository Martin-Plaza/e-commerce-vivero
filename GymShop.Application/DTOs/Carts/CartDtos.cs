using System.ComponentModel.DataAnnotations;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Payments;

namespace GymShop.Application.DTOs.Carts;

public record AddCartItemRequest(int ProductId, int Quantity, int? ProductVariantId = null);
public record UpdateCartItemRequest(int Quantity);
public record CheckoutCartRequest(
    [Required, StringLength(30)] string DeliveryMethod,
    [StringLength(ValidationLimits.ShippingAddress)] string? ShippingAddress,
    [NonNegativeSqlDecimal] decimal ExpectedShippingCost,
    [SqlDecimal] decimal? ExpectedSubtotal = null,
    [NonNegativeSqlDecimal] decimal? ExpectedDiscount = null,
    [StringLength(ValidationLimits.IdempotencyKey)] string? IdempotencyKey = null,
    Guid? ShippingQuoteId = null,
    ShippingAddressRequest? ShippingDestination = null,
    [StringLength(ValidationLimits.PaymentProvider)] string? PaymentProvider = null,
    [StringLength(ValidationLimits.IdempotencyKey)] string? PaymentIdempotencyKey = null);

public sealed record CheckoutResponse(
    int Id,
    int? OrderId,
    int? UserId,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    decimal Subtotal,
    string? CouponCode,
    decimal DiscountAmount,
    string DeliveryMethod,
    decimal ShippingCost,
    decimal Total,
    string Status,
    string ShippingAddress,
    string PickupAddress,
    string PickupHours,
    string PickupInstructions,
    List<OrderItemResponse> Items,
    PaymentResponse? Payment,
    OrderShippingAddressResponse? ShippingDestination = null,
    string? ShippingProviderCode = null,
    string? ShippingServiceCode = null,
    string? ShippingServiceName = null);

public sealed record ShippingAddressRequest(
    [Required, StringLength(ValidationLimits.ShippingPostalCode)] string PostalCode,
    [Required, StringLength(ValidationLimits.ShippingProvince)] string Province,
    [Required, StringLength(ValidationLimits.ShippingCity)] string City,
    [Required, StringLength(ValidationLimits.ShippingStreet)] string Street,
    [Required, StringLength(ValidationLimits.ShippingStreetNumber)] string StreetNumber,
    [StringLength(ValidationLimits.ShippingFloor)] string? Floor = null,
    [StringLength(ValidationLimits.ShippingApartment)] string? Apartment = null,
    [StringLength(ValidationLimits.ShippingNotes)] string? Notes = null);

public sealed record CreateShippingQuoteRequest([Required] ShippingAddressRequest Destination);

public sealed record ShippingQuoteResponse(
    Guid Id,
    string ProviderCode,
    string ServiceCode,
    string ServiceName,
    decimal Price,
    DateTime EstimatedDeliveryFrom,
    DateTime EstimatedDeliveryTo,
    DateTime ExpiresAtUtc);

public record ShippingOptionsResponse(decimal HomeDeliveryCost, string PickupAddress, string PickupInstructions, string PickupHours);

public record CartResponse(int Id, int UserId, decimal Subtotal, decimal Discount, decimal Total, string? CouponCode, List<CartItemResponse> Items);
public record CartItemResponse(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Subtotal, int Stock, string? ImageUrl,
    int? ProductVariantId = null, string? VariantSku = null, Dictionary<string, string>? VariantAttributes = null);

public sealed record GuestCustomerRequest(
    [Required, StringLength(ValidationLimits.UserName)] string FirstName,
    [Required, StringLength(ValidationLimits.UserName)] string LastName,
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email,
    [Required, Phone, StringLength(ValidationLimits.Phone)] string Phone);

public sealed record GuestCartItemRequest(
    [Range(1, int.MaxValue)] int ProductId,
    [Range(1, 100)] int Quantity,
    int? ProductVariantId = null);

public sealed record GuestCheckoutRequest(
    [Required] GuestCustomerRequest Customer,
    [Required, MinLength(1)] List<GuestCartItemRequest> Items,
    [Required, StringLength(30)] string DeliveryMethod,
    ShippingAddressRequest? ShippingDestination,
    [NonNegativeSqlDecimal] decimal ExpectedShippingCost,
    [SqlDecimal] decimal ExpectedSubtotal,
    [Required, StringLength(ValidationLimits.PaymentProvider)] string PaymentProvider,
    [Required, StringLength(ValidationLimits.IdempotencyKey)] string IdempotencyKey);

public sealed record GuestCheckoutResponse(
    string Kind,
    Guid AccessToken,
    DateTime ExpiresAt,
    OrderResponse? Order,
    CheckoutResponse? Checkout);
