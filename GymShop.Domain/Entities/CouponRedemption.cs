using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class CouponRedemption
{
    public long Id { get; set; }
    public int CouponId { get; set; }
    public int UserId { get; set; }
    public int OrderId { get; set; }
    public CouponRedemptionStatus Status { get; set; } = CouponRedemptionStatus.Reserved;
    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public Coupon Coupon { get; set; } = null!;
    public User User { get; set; } = null!;
    public Order Order { get; set; } = null!;
}

