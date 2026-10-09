using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Users;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Users;

public interface IGetUsersUseCase { Task<AppResult<PagedUsersResponse>> ExecuteAsync(UserFilterRequest filter, CancellationToken cancellationToken = default); }
public interface IGetUserByIdUseCase { Task<AppResult<AdminUserDetailResponse>> ExecuteAsync(int id, CancellationToken cancellationToken = default); }
public interface ICreateUserUseCase { Task<AppResult<AdminUserResponse>> ExecuteAsync(CreateUserRequest request, CancellationToken cancellationToken = default); }
public interface IUpdateUserRoleUseCase { Task<AppResult> ExecuteAsync(int id, UpdateUserRoleRequest request, int? currentUserId = null, CancellationToken cancellationToken = default); }
public interface IUpdateUserStatusUseCase { Task<AppResult> ExecuteAsync(int id, UpdateUserStatusRequest request, int? currentUserId = null, CancellationToken cancellationToken = default); }

public sealed class GetUsersUseCase(IApplicationDbContext db) : IGetUsersUseCase
{
    public async Task<AppResult<PagedUsersResponse>> ExecuteAsync(UserFilterRequest filter, CancellationToken cancellationToken = default)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100) return AppResult<PagedUsersResponse>.Failure(AppErrorType.Validation, "La paginacion solicitada no es valida.");
        var role = string.IsNullOrWhiteSpace(filter.Role) ? null : UserRoleLookup.NormalizeRole(filter.Role);
        if (!string.IsNullOrWhiteSpace(filter.Role) && role is null) return AppResult<PagedUsersResponse>.Failure(AppErrorType.Validation, "Rol invalido.");
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var search = filter.Search.Trim().ToLowerInvariant(); query = query.Where(x => x.Email.ToLower().Contains(search) || (x.Name + " " + (x.LastName ?? "")).ToLower().Contains(search)); }
        if (role is not null) query = query.Where(x => x.Role.Name == role);
        if (filter.IsActive.HasValue) query = query.Where(x => x.IsActive == filter.IsActive.Value);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(x => new AdminUserResponse(x.Id, x.Email, (x.Name + " " + (x.LastName ?? "")).Trim(), x.Role.Name, x.IsActive, x.CreatedAt)).ToListAsync(cancellationToken);
        return AppResult<PagedUsersResponse>.Success(new(items, filter.Page, filter.PageSize, total, total == 0 ? 0 : (int)Math.Ceiling(total / (double)filter.PageSize)));
    }
}

public sealed class GetUserByIdUseCase(IApplicationDbContext db) : IGetUserByIdUseCase
{
    private const int RecentOrdersPageSize = 10;
    private static readonly OrderStatus[] CommercialStatuses = [OrderStatus.Paid, OrderStatus.Preparing, OrderStatus.Shipped, OrderStatus.Delivered];
    public async Task<AppResult<AdminUserDetailResponse>> ExecuteAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.Email, Name = (x.Name + " " + (x.LastName ?? "")).Trim(), Role = x.Role.Name, x.IsActive, x.CreatedAt }).SingleOrDefaultAsync(cancellationToken);
        if (user is null) return AppResult<AdminUserDetailResponse>.Failure(AppErrorType.NotFound, "Usuario no encontrado.");
        var valid = db.Orders.AsNoTracking().Where(x => x.UserId == id && CommercialStatuses.Contains(x.Status));
        var metrics = await valid.GroupBy(_ => 1).Select(g => new { Count = g.LongCount(), Total = g.Sum(x => x.Total), Last = (DateTime?)g.Max(x => x.CreatedAt) }).SingleOrDefaultAsync(cancellationToken);
        var allOrders = db.Orders.AsNoTracking().Where(x => x.UserId == id);
        var ordersTotal = await allOrders.LongCountAsync(cancellationToken);
        var recentOrders = await allOrders.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(RecentOrdersPageSize).Select(x => new UserOrderSummaryResponse(x.Id, x.CreatedAt, x.Total, x.Status.ToString(), x.DeliveryMethod.ToString())).ToListAsync(cancellationToken);
        return AppResult<AdminUserDetailResponse>.Success(new(user.Id, user.Email, user.Name, user.Role, user.IsActive, user.CreatedAt, metrics?.Count ?? 0, metrics?.Total ?? 0, metrics?.Last, ordersTotal, RecentOrdersPageSize, recentOrders));
    }
}

public sealed class CreateUserUseCase(IApplicationDbContext db, IPasswordHasher passwordHasher, IAuditContext? auditContext = null) : ICreateUserUseCase
{
    public async Task<AppResult<AdminUserResponse>> ExecuteAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant(); var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password)) return AppResult<AdminUserResponse>.Failure(AppErrorType.Validation, "Nombre, email y password son obligatorios.");
        if (request.Password.Length < 6) return AppResult<AdminUserResponse>.Failure(AppErrorType.Validation, "La password debe tener al menos 6 caracteres.");
        if (await db.Users.AnyAsync(x => x.Email.ToLower() == email, cancellationToken)) return AppResult<AdminUserResponse>.Failure(AppErrorType.Conflict, "El email ya esta registrado.");
        var role = await UserRoleLookup.FindRoleAsync(db, request.Role, cancellationToken);
        if (role is null) return AppResult<AdminUserResponse>.Failure(AppErrorType.Validation, "Rol invalido.");
        var user = new User { Name = name, Email = email, PasswordHash = passwordHasher.Hash(request.Password), RoleId = role.Id, Role = role, IsActive = true, EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user); await db.SaveChangesAsync(cancellationToken);
        AuditTrail.Add(db, auditContext, "UserCreated", "User", user.Id, null, new { user.Email, role = role.Name, user.IsActive }); await db.SaveChangesAsync(cancellationToken);
        return AppResult<AdminUserResponse>.Success(UserAdminMapper.ToResponse(user));
    }
}

public sealed class UpdateUserRoleUseCase(IApplicationDbContext db, IAuditContext? auditContext = null, ITransactionManager? transactionManager = null) : IUpdateUserRoleUseCase
{
    public async Task<AppResult> ExecuteAsync(int id, UpdateUserRoleRequest request, int? currentUserId = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await UserAdministrationTransactions.BeginAsync(transactionManager, cancellationToken);
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == id, cancellationToken); if (user is null) return AppResult.Failure(AppErrorType.NotFound, "Usuario no encontrado.");
        var role = await UserRoleLookup.FindRoleAsync(db, request.Role, cancellationToken); if (role is null) return AppResult.Failure(AppErrorType.Validation, "Rol invalido.");
        if (user.Role.Name == "SuperAdmin" && role.Name != "SuperAdmin" && user.IsActive && !await HasAnotherActiveSuperAdmin(db, id, cancellationToken)) return AppResult.Failure(AppErrorType.Conflict, currentUserId == id ? "No podes degradarte siendo el ultimo SuperAdmin activo." : "No se puede degradar al ultimo SuperAdmin activo.");
        if (user.RoleId != role.Id) { var old = user.Role.Name; user.RoleId = role.Id; user.Role = role; user.TokenVersion++; user.UpdatedAt = DateTime.UtcNow; AuditTrail.Add(db, auditContext, "UserRoleChanged", "User", user.Id, new { role = old }, new { role = role.Name }); await db.SaveChangesAsync(cancellationToken); }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return AppResult.Success();
    }
    internal static Task<bool> HasAnotherActiveSuperAdmin(IApplicationDbContext db, int id, CancellationToken ct) => db.Users.AsNoTracking().AnyAsync(x => x.Id != id && x.IsActive && x.Role.Name == "SuperAdmin", ct);
}

public sealed class UpdateUserStatusUseCase(IApplicationDbContext db, IAuditContext? auditContext = null, ITransactionManager? transactionManager = null) : IUpdateUserStatusUseCase
{
    public async Task<AppResult> ExecuteAsync(int id, UpdateUserStatusRequest request, int? currentUserId = null, CancellationToken cancellationToken = default)
    {
        await using var transaction = await UserAdministrationTransactions.BeginAsync(transactionManager, cancellationToken);
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == id, cancellationToken); if (user is null) return AppResult.Failure(AppErrorType.NotFound, "Usuario no encontrado.");
        if (currentUserId == id && !request.IsActive) return AppResult.Failure(AppErrorType.Conflict, "No podes desactivar tu propio usuario.");
        if (!request.IsActive && user.IsActive && user.Role.Name == "SuperAdmin" && !await UpdateUserRoleUseCase.HasAnotherActiveSuperAdmin(db, id, cancellationToken)) return AppResult.Failure(AppErrorType.Conflict, "No se puede desactivar al ultimo SuperAdmin activo.");
        if (user.IsActive != request.IsActive) { var old = user.IsActive; user.IsActive = request.IsActive; user.TokenVersion++; user.UpdatedAt = DateTime.UtcNow; AuditTrail.Add(db, auditContext, "UserStatusChanged", "User", user.Id, new { isActive = old }, new { user.IsActive }); await db.SaveChangesAsync(cancellationToken); }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return AppResult.Success();
    }
}

internal static class UserAdministrationTransactions
{
    public static async Task<IApplicationTransaction?> BeginAsync(ITransactionManager? manager, CancellationToken cancellationToken) =>
        manager is null ? null : await manager.BeginUserAdministrationTransactionAsync(cancellationToken);
}

internal static class UserRoleLookup
{
    public static async Task<Role?> FindRoleAsync(IApplicationDbContext db, string role, CancellationToken ct) { var normalized = NormalizeRole(role); return normalized is null ? null : await db.Roles.SingleOrDefaultAsync(x => x.Name == normalized, ct); }
    public static string? NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch { "user" => "User", "admin" => "Admin", "superadmin" => "SuperAdmin", _ => null };
}
internal static class UserAdminMapper { public static AdminUserResponse ToResponse(User user) => new(user.Id, user.Email, user.Name, user.Role.Name, user.IsActive, user.CreatedAt); }
