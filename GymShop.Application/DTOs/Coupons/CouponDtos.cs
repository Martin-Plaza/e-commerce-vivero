using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Coupons;

public sealed record CouponFilterRequest(int Page = 1, int PageSize = 20, string? Search = null, string? Status = null, string? Type = null, string? Validity = null);
public sealed record UpsertCouponRequest([Required, StringLength(50)] string Code, [Required, StringLength(200)] string Name, [Required] string Type, decimal Value, decimal? MinimumPurchase, decimal? MaximumDiscount, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int? TotalUsageLimit, int? UsageLimitPerUser, bool IsActive = true);
public sealed record UpdateCouponStatusRequest(bool IsActive);
public sealed record ApplyCouponRequest([Required, StringLength(50)] string Code);
public sealed record CouponResponse(int Id, string Code, string Name, string Type, decimal Value, decimal? MinimumPurchase, decimal? MaximumDiscount, DateTime? StartsAtUtc, DateTime? EndsAtUtc, int? TotalUsageLimit, int? UsageLimitPerUser, bool IsActive, int ReservedUses, int ConsumedUses, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
public sealed record PagedCouponsResponse(List<CouponResponse> Items, int Page, int PageSize, long TotalItems, int TotalPages);

