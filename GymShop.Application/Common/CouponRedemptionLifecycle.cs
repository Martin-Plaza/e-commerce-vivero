using GymShop.Domain.Entities;
using GymShop.Domain.Enums;

namespace GymShop.Application.Common;

public static class CouponRedemptionLifecycle
{
    public static bool Consume(Order order)
    {
        var redemption = order.CouponRedemption;
        if (redemption is null || redemption.Status != CouponRedemptionStatus.Reserved) return false;
        redemption.Status = CouponRedemptionStatus.Consumed;
        redemption.ConsumedAtUtc = DateTime.UtcNow;
        return true;
    }

    public static bool Release(Order order)
    {
        var redemption = order.CouponRedemption;
        if (redemption is null || redemption.Status != CouponRedemptionStatus.Reserved) return false;
        redemption.Status = CouponRedemptionStatus.Released;
        redemption.ReleasedAtUtc = DateTime.UtcNow;
        return true;
    }

    public static bool ReleaseConsumedAfterCancellation(Order order)
    {
        var redemption = order.CouponRedemption;
        if (redemption is null || redemption.Status != CouponRedemptionStatus.Consumed) return false;
        redemption.Status = CouponRedemptionStatus.Released;
        redemption.ReleasedAtUtc = DateTime.UtcNow;
        return true;
    }
}
