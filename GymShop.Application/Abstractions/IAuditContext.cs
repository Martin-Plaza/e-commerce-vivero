namespace GymShop.Application.Abstractions;

public interface IAuditContext
{
    int? ActorUserId { get; }
    string CorrelationId { get; }
}

public sealed class SystemAuditContext : IAuditContext
{
    public static SystemAuditContext Instance { get; } = new();
    private SystemAuditContext() { }
    public int? ActorUserId => null;
    public string CorrelationId => $"system-{Guid.NewGuid():N}";
}
