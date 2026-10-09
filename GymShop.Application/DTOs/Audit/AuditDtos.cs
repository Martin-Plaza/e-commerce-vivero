using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Audit;

public sealed record AuditQueryRequest(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 50,
    [StringLength(100)] string? Action = null,
    [StringLength(50)] string? EntityType = null,
    [StringLength(100)] string? EntityId = null,
    int? ActorUserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record AuditEntryResponse(long Id, int? ActorUserId, string Action, string EntityType,
    string EntityId, string? OldValue, string? NewValue, string? Reason, DateTime CreatedAtUtc, string CorrelationId,
    string? ActorName, string? EntityDisplayName);

public sealed record PagedAuditResponse(List<AuditEntryResponse> Items, int Page, int PageSize, long TotalItems, int TotalPages);
