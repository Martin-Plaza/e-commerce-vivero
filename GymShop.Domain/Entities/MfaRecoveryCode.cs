namespace GymShop.Domain.Entities;

public sealed class MfaRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }

    public User User { get; set; } = null!;
}
