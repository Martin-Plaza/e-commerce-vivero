using GymShop.Application.Common;
using GymShop.Application.DTOs.Coupons;
using GymShop.Application.UseCases.Coupons;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;

namespace GymShop.Tests.UseCases;

public class CouponUseCaseTests
{
    [Fact] public void Percentage_is_rounded_consistently() => Assert.Equal(1.01m, CouponRules.CalculateDiscount(Coupon(CouponType.Percentage, 10m), 10.05m));
    [Fact] public void Fixed_amount_is_applied() => Assert.Equal(25m, CouponRules.CalculateDiscount(Coupon(CouponType.FixedAmount, 25m), 100m));
    [Fact] public void Discount_never_exceeds_subtotal() => Assert.Equal(40m, CouponRules.CalculateDiscount(Coupon(CouponType.FixedAmount, 100m), 40m));
    [Fact] public void Percentage_honors_maximum_discount() { var coupon = Coupon(CouponType.Percentage, 50m); coupon.MaximumDiscount = 20m; Assert.Equal(20m, CouponRules.CalculateDiscount(coupon, 100m)); }
    [Fact] public void Code_is_normalized() => Assert.Equal("VERANO25", CouponRules.NormalizeCode(" verano25 "));

    [Theory]
    [InlineData(false, null, null, "inactivo")]
    [InlineData(true, 1, null, "todavía")]
    [InlineData(true, null, -1, "vencido")]
    public void Availability_rejects_invalid_dates_and_status(bool active, int? startsOffset, int? endsOffset, string expected)
    {
        var now = DateTime.UtcNow; var coupon = Coupon(CouponType.FixedAmount, 10m); coupon.IsActive = active; coupon.StartsAtUtc = startsOffset is null ? null : now.AddDays(startsOffset.Value); coupon.EndsAtUtc = endsOffset is null ? null : now.AddDays(endsOffset.Value);
        Assert.Contains(expected, CouponRules.ValidateAvailability(coupon, 100m, now, 0, 0)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void Availability_rejects_minimum_purchase() { var c = Coupon(CouponType.FixedAmount, 10); c.MinimumPurchase = 101; Assert.Contains("mínima", CouponRules.ValidateAvailability(c, 100, DateTime.UtcNow, 0, 0)!); }
    [Fact] public void Availability_rejects_total_limit() { var c = Coupon(CouponType.FixedAmount, 10); c.TotalUsageLimit = 1; Assert.Contains("agotó", CouponRules.ValidateAvailability(c, 100, DateTime.UtcNow, 1, 0)!); }
    [Fact] public void Availability_rejects_user_limit() { var c = Coupon(CouponType.FixedAmount, 10); c.UsageLimitPerUser = 1; Assert.Contains("límite", CouponRules.ValidateAvailability(c, 100, DateTime.UtcNow, 0, 1)!); }

    [Fact]
    public void Lifecycle_is_idempotent_and_refunds_do_not_release_consumed_uses()
    {
        var redemption = new CouponRedemption(); var order = new Order { CouponRedemption = redemption };
        Assert.True(CouponRedemptionLifecycle.Consume(order)); Assert.False(CouponRedemptionLifecycle.Consume(order)); Assert.False(CouponRedemptionLifecycle.Release(order)); Assert.Equal(CouponRedemptionStatus.Consumed, redemption.Status);
    }

    [Fact]
    public void Release_is_idempotent()
    {
        var redemption = new CouponRedemption(); var order = new Order { CouponRedemption = redemption };
        Assert.True(CouponRedemptionLifecycle.Release(order)); Assert.False(CouponRedemptionLifecycle.Release(order)); Assert.Equal(CouponRedemptionStatus.Released, redemption.Status);
    }

    [Fact]
    public void Validation_rejects_invalid_percentage_and_date_range()
    {
        var invalidValue = Request(value: 101); Assert.NotNull(CouponRules.Validate(invalidValue, CouponType.Percentage));
        var invalidDates = Request(starts: DateTime.UtcNow, ends: DateTime.UtcNow.AddMinutes(-1)); Assert.Contains("posterior", CouponRules.Validate(invalidDates, CouponType.Percentage)!);
    }

    private static Coupon Coupon(CouponType type, decimal value) => new() { Code = "TEST", Name = "Test", Type = type, Value = value, IsActive = true };
    private static UpsertCouponRequest Request(decimal value = 10, DateTime? starts = null, DateTime? ends = null) => new("TEST", "Test", "Percentage", value, null, null, starts, ends, null, null);
}
