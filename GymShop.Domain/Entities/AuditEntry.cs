namespace GymShop.Domain.Entities;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public int? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = string.Empty;

    public User? ActorUser { get; set; }
}
