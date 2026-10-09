namespace GymShop.Domain.Entities;

public sealed class UserExternalLogin
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderSubject { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public User User { get; set; } = null!;
}
