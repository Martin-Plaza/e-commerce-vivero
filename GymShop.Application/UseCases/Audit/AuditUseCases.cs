using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Audit;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Audit;

public interface IGetAuditEntriesUseCase
{
    Task<AppResult<PagedAuditResponse>> ExecuteAsync(AuditQueryRequest request, CancellationToken cancellationToken = default);
}

public sealed class GetAuditEntriesUseCase : IGetAuditEntriesUseCase
{
    private readonly IApplicationDbContext _db;
    public GetAuditEntriesUseCase(IApplicationDbContext db) => _db = db;

    public async Task<AppResult<PagedAuditResponse>> ExecuteAsync(AuditQueryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            return AppResult<PagedAuditResponse>.Failure(AppErrorType.Validation, "La paginacion solicitada no es valida.");
        if (request.FromUtc > request.ToUtc)
            return AppResult<PagedAuditResponse>.Failure(AppErrorType.Validation, "El rango de fechas no es valido.");

        var query = _db.AuditEntries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(x => x.Action == request.Action.Trim());
        if (!string.IsNullOrWhiteSpace(request.EntityType)) query = query.Where(x => x.EntityType == request.EntityType.Trim());
        if (!string.IsNullOrWhiteSpace(request.EntityId)) query = query.Where(x => x.EntityId == request.EntityId.Trim());
        if (request.ActorUserId.HasValue) query = query.Where(x => x.ActorUserId == request.ActorUserId);
        if (request.FromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc <= request.ToUtc);

        var total = await query.LongCountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new
            {
                x.Id, x.ActorUserId, x.Action, x.EntityType, x.EntityId, x.OldValue, x.NewValue, x.Reason,
                x.CreatedAtUtc, x.CorrelationId,
                ActorName = x.ActorUser == null ? null : (x.ActorUser.Name + " " + (x.ActorUser.LastName ?? "")).Trim()
            })
            .ToListAsync(cancellationToken);

        var userEntityIds = rows
            .Where(x => x.EntityType == "User" && int.TryParse(x.EntityId, out _))
            .Select(x => int.Parse(x.EntityId)).Distinct().ToList();
        var userNames = userEntityIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().Where(x => userEntityIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => (x.Name + " " + (x.LastName ?? "")).Trim(), cancellationToken);
        var items = rows.Select(x => new AuditEntryResponse(
            x.Id, x.ActorUserId, x.Action, x.EntityType, x.EntityId, x.OldValue, x.NewValue, x.Reason,
            x.CreatedAtUtc, x.CorrelationId, x.ActorName,
            x.EntityType == "User" && int.TryParse(x.EntityId, out var userId) && userNames.TryGetValue(userId, out var userName)
                ? userName
                : null)).ToList();
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)request.PageSize);
        return AppResult<PagedAuditResponse>.Success(new PagedAuditResponse(items, request.Page, request.PageSize, total, pages));
    }
}
