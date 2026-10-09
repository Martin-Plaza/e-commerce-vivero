using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class Coupon
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CouponType Type { get; set; }
    public decimal Value { get; set; }
    public decimal? MinimumPurchase { get; set; }
    public decimal? MaximumDiscount { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public int? TotalUsageLimit { get; set; }
    public int? UsageLimitPerUser { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public ICollection<CouponRedemption> Redemptions { get; set; } = new List<CouponRedemption>();
}

