namespace GymShop.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public int TokenVersion { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public string? MfaSecretEncrypted { get; set; }
    public DateTime? MfaEnabledAtUtc { get; set; }
    public long? MfaLastUsedTimeStep { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Role Role { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<CheckoutSession> CheckoutSessions { get; set; } = new List<CheckoutSession>();
    public Cart? Cart { get; set; }
    public ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();
    public ICollection<EmailVerificationCode> EmailVerificationCodes { get; set; } = new List<EmailVerificationCode>();
    public ICollection<PasswordResetCode> PasswordResetCodes { get; set; } = new List<PasswordResetCode>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<MfaRecoveryCode> MfaRecoveryCodes { get; set; } = new List<MfaRecoveryCode>();
}


