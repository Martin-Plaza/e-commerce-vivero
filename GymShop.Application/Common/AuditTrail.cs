using System.Text.Json;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;

namespace GymShop.Application.Common;

public static class AuditTrail
{
    public static void Add(
        IApplicationDbContext db,
        IAuditContext? context,
        string action,
        string entityType,
        object entityId,
        object? oldValue,
        object? newValue,
        string? reason = null)
    {
        context ??= SystemAuditContext.Instance;
        var entry = new AuditEntry
        {
            ActorUserId = context.ActorUserId,
            Action = Limit(action, 100)!,
            EntityType = Limit(entityType, 50)!,
            EntityId = Limit(Convert.ToString(entityId, System.Globalization.CultureInfo.InvariantCulture), 100)!,
            OldValue = Serialize(oldValue),
            NewValue = Serialize(newValue),
            Reason = Limit(string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), 500),
            CreatedAtUtc = DateTime.UtcNow,
            CorrelationId = Limit(context.CorrelationId, 100) ?? "unknown"
        };
        db.AuditEntries.Add(entry);
    }

    private static string? Serialize(object? value)
    {
        if (value is null) return null;
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        if (json.Length > 2000) throw new InvalidOperationException("Audit value exceeds the safe 2000 character limit.");
        return json;
    }

    private static string? Limit(string? value, int max) => value is null ? null : value[..Math.Min(value.Length, max)];
}
