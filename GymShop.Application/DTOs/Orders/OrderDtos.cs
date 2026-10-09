using System.ComponentModel.DataAnnotations;
using GymShop.Application.Common;

namespace GymShop.Application.DTOs.Orders;

public record UpdateOrderStatusRequest(
    [Required, StringLength(30)] string Status,
    DateTime? ExpectedUpdatedAt = null,
    [StringLength(ValidationLimits.Carrier)] string? Carrier = null,
    [StringLength(ValidationLimits.TrackingNumber)] string? TrackingNumber = null,
    [StringLength(ValidationLimits.TrackingUrl)] string? TrackingUrl = null);

public sealed record OrderFilterRequest(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 20,
    [StringLength(150)] string? Search = null,
    [StringLength(30)] string? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public record CancelOrderRequest([StringLength(ValidationLimits.CancellationReason)] string? Reason);

public record ExpirePendingOrdersRequest(int OlderThanMinutes);

public record ExpirePendingOrdersResponse(int CanceledOrders);

public record OrderResponse(
    int Id,
    int? UserId,
    string? UserEmail,
    string UserName,
    DateTime CreatedAt,
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
    string? Carrier,
    string? TrackingNumber,
    string? TrackingUrl,
    string? CancellationReason,
    DateTime? UpdatedAt,
    List<OrderItemResponse> Items,
    List<OrderPaymentResponse> Payments,
    OrderShippingAddressResponse? ShippingDestination = null,
    string? ShippingProviderCode = null,
    string? ShippingServiceCode = null,
    string? ShippingServiceName = null,
    string? CustomerPhone = null,
    DateTime? ExpiresAt = null
);

public sealed record OrderShippingAddressResponse(
    string PostalCode,
    string Province,
    string City,
    string Street,
    string StreetNumber,
    string? Floor,
    string? Apartment,
    string? Notes);

public record OrderSummaryResponse(
    int Id,
    int? UserId,
    string? UserEmail,
    string UserName,
    DateTime CreatedAt,
    decimal Total,
    string DeliveryMethod,
    string Status,
    DateTime? UpdatedAt,
    string? LastPaymentStatus,
    int? LastPaymentId
);

public sealed record PagedOrdersResponse(
    List<OrderSummaryResponse> Items,
    int Page,
    int PageSize,
    long TotalItems,
    int TotalPages);

public sealed record OrderHistoryEventResponse(
    long Id,
    string Action,
    string? PreviousStatus,
    string? NewStatus,
    string? Reason,
    DateTime CreatedAtUtc,
    int? ActorUserId,
    string? ActorName,
    string? ActorEmail,
    string Source);

public record OrderItemResponse(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal Subtotal,
    int? ProductVariantId = null,
    string? VariantSku = null,
    Dictionary<string, string>? VariantAttributes = null
);

public record OrderPaymentResponse(
    int Id,
    string Provider,
    string ExternalReference,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? FailureReason,
    bool RequiresReview
);



