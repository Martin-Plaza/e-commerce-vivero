namespace GymShop.Domain.Entities;

public sealed class WebhookReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = string.Empty;
    public string RequestIdHash { get; set; } = string.Empty;
    public DateTime SignedAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}
