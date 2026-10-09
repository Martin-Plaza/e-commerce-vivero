using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Coupons;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Coupons;

public interface IGetCouponsUseCase { Task<AppResult<PagedCouponsResponse>> ExecuteAsync(CouponFilterRequest request, CancellationToken ct = default); }
public interface IGetCouponUseCase { Task<AppResult<CouponResponse>> ExecuteAsync(int id, CancellationToken ct = default); }
public interface ICreateCouponUseCase { Task<AppResult<CouponResponse>> ExecuteAsync(UpsertCouponRequest request, CancellationToken ct = default); }
public interface IUpdateCouponUseCase { Task<AppResult<CouponResponse>> ExecuteAsync(int id, UpsertCouponRequest request, CancellationToken ct = default); }
public interface IUpdateCouponStatusUseCase { Task<AppResult> ExecuteAsync(int id, UpdateCouponStatusRequest request, CancellationToken ct = default); }
public interface IApplyCartCouponUseCase { Task<AppResult> ExecuteAsync(int userId, ApplyCouponRequest request, CancellationToken ct = default); }
public interface IRemoveCartCouponUseCase { Task<AppResult> ExecuteAsync(int userId, CancellationToken ct = default); }

public static class CouponRules
{
    public static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    public static decimal CalculateDiscount(Coupon coupon, decimal subtotal) => Math.Min(subtotal, Math.Round(coupon.Type == CouponType.Percentage ? Math.Min(subtotal * coupon.Value / 100m, coupon.MaximumDiscount ?? decimal.MaxValue) : coupon.Value, 2, MidpointRounding.AwayFromZero));
    public static string? ValidateAvailability(Coupon coupon, decimal subtotal, DateTime now, int activeUses, int userUses)
    {
        if (!coupon.IsActive) return "El cupón está inactivo.";
        if (coupon.StartsAtUtc is { } starts && starts > now) return "El cupón todavía no está vigente.";
        if (coupon.EndsAtUtc is { } ends && ends <= now) return "El cupón está vencido.";
        if (coupon.MinimumPurchase is { } minimum && subtotal < minimum) return $"La compra mínima para este cupón es {minimum:0.00}.";
        if (coupon.TotalUsageLimit is { } total && activeUses >= total) return "El cupón agotó su límite de usos.";
        if (coupon.UsageLimitPerUser is { } perUser && userUses >= perUser) return "Ya alcanzaste el límite de usos de este cupón.";
        return null;
    }
    public static string? Validate(UpsertCouponRequest r, CouponType? type)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return "El código es obligatorio.";
        if (string.IsNullOrWhiteSpace(r.Name)) return "El nombre es obligatorio.";
        if (type is null) return "El tipo debe ser Percentage o FixedAmount.";
        if (r.Value <= 0 || type == CouponType.Percentage && r.Value > 100) return "El valor del cupón no es válido.";
        if (r.MinimumPurchase < 0 || r.MaximumDiscount <= 0 || r.TotalUsageLimit <= 0 || r.UsageLimitPerUser <= 0) return "Los límites opcionales deben ser positivos.";
        if (type != CouponType.Percentage && r.MaximumDiscount is not null) return "El descuento máximo solo corresponde a cupones porcentuales.";
        if (r.StartsAtUtc is { } start && r.EndsAtUtc is { } end && end <= start) return "La fecha de finalización debe ser posterior a la de inicio.";
        return null;
    }
    public static CouponResponse Map(Coupon x) => new(x.Id, x.Code, x.Name, x.Type.ToString(), x.Value, x.MinimumPurchase, x.MaximumDiscount, x.StartsAtUtc, x.EndsAtUtc, x.TotalUsageLimit, x.UsageLimitPerUser, x.IsActive, x.Redemptions.Count(r => r.Status == CouponRedemptionStatus.Reserved), x.Redemptions.Count(r => r.Status == CouponRedemptionStatus.Consumed), x.CreatedAtUtc, x.UpdatedAtUtc);
}

public class GetCouponsUseCase(IApplicationDbContext db) : IGetCouponsUseCase
{
    public async Task<AppResult<PagedCouponsResponse>> ExecuteAsync(CouponFilterRequest r, CancellationToken ct = default)
    {
        var page = Math.Max(1, r.Page); var size = Math.Clamp(r.PageSize, 1, 100); var now = DateTime.UtcNow;
        var q = db.Coupons.AsNoTracking().Include(x => x.Redemptions).AsQueryable();
        if (!string.IsNullOrWhiteSpace(r.Search)) { var s = r.Search.Trim().ToLower(); q = q.Where(x => x.Code.ToLower().Contains(s) || x.Name.ToLower().Contains(s)); }
        if (bool.TryParse(r.Status, out var active)) q = q.Where(x => x.IsActive == active);
        if (Enum.TryParse<CouponType>(r.Type, true, out var type)) q = q.Where(x => x.Type == type);
        q = r.Validity?.ToLowerInvariant() switch { "current" => q.Where(x => (x.StartsAtUtc == null || x.StartsAtUtc <= now) && (x.EndsAtUtc == null || x.EndsAtUtc > now)), "future" => q.Where(x => x.StartsAtUtc > now), "expired" => q.Where(x => x.EndsAtUtc <= now), _ => q };
        var total = await q.LongCountAsync(ct); var items = await q.OrderByDescending(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return AppResult<PagedCouponsResponse>.Success(new(items.Select(CouponRules.Map).ToList(), page, size, total, (int)Math.Ceiling(total / (double)size)));
    }
}
public class GetCouponUseCase(IApplicationDbContext db) : IGetCouponUseCase { public async Task<AppResult<CouponResponse>> ExecuteAsync(int id, CancellationToken ct = default) { var x = await db.Coupons.AsNoTracking().Include(c => c.Redemptions).SingleOrDefaultAsync(c => c.Id == id, ct); return x is null ? AppResult<CouponResponse>.Failure(AppErrorType.NotFound, "Cupón no encontrado.") : AppResult<CouponResponse>.Success(CouponRules.Map(x)); } }

public abstract class CouponWriteUseCase(IApplicationDbContext db, IAuditContext? audit)
{
    protected IApplicationDbContext Db => db; protected IAuditContext? Audit => audit;
    protected static void Assign(Coupon x, UpsertCouponRequest r, CouponType type) { x.Code = CouponRules.NormalizeCode(r.Code); x.Name = r.Name.Trim(); x.Type = type; x.Value = r.Value; x.MinimumPurchase = r.MinimumPurchase; x.MaximumDiscount = r.MaximumDiscount; x.StartsAtUtc = r.StartsAtUtc; x.EndsAtUtc = r.EndsAtUtc; x.TotalUsageLimit = r.TotalUsageLimit; x.UsageLimitPerUser = r.UsageLimitPerUser; x.IsActive = r.IsActive; }
}
public class CreateCouponUseCase(IApplicationDbContext db, IAuditContext? audit = null) : CouponWriteUseCase(db, audit), ICreateCouponUseCase
{
    public async Task<AppResult<CouponResponse>> ExecuteAsync(UpsertCouponRequest r, CancellationToken ct = default) { var parsed = Enum.TryParse<CouponType>(r.Type, true, out var type); var error = CouponRules.Validate(r, parsed ? type : null); if (error is not null) return AppResult<CouponResponse>.Failure(AppErrorType.Validation, error); var code = CouponRules.NormalizeCode(r.Code); if (await Db.Coupons.AnyAsync(x => x.Code == code, ct)) return AppResult<CouponResponse>.Failure(AppErrorType.Conflict, "Ya existe un cupón con ese código."); var x = new Coupon(); Assign(x, r, type); Db.Coupons.Add(x); await Db.SaveChangesAsync(ct); AuditTrail.Add(Db, Audit, "CouponCreated", "Coupon", x.Id, null, new { x.Code, x.Name, x.Type, x.Value, x.IsActive }); await Db.SaveChangesAsync(ct); return AppResult<CouponResponse>.Success(CouponRules.Map(x)); }
}
public class UpdateCouponUseCase(IApplicationDbContext db, IAuditContext? audit = null) : CouponWriteUseCase(db, audit), IUpdateCouponUseCase
{
    public async Task<AppResult<CouponResponse>> ExecuteAsync(int id, UpsertCouponRequest r, CancellationToken ct = default) { var x = await Db.Coupons.Include(c => c.Redemptions).SingleOrDefaultAsync(c => c.Id == id, ct); if (x is null) return AppResult<CouponResponse>.Failure(AppErrorType.NotFound, "Cupón no encontrado."); var parsed = Enum.TryParse<CouponType>(r.Type, true, out var type); var error = CouponRules.Validate(r, parsed ? type : null); if (error is not null) return AppResult<CouponResponse>.Failure(AppErrorType.Validation, error); var code = CouponRules.NormalizeCode(r.Code); if (await Db.Coupons.AnyAsync(c => c.Id != id && c.Code == code, ct)) return AppResult<CouponResponse>.Failure(AppErrorType.Conflict, "Ya existe un cupón con ese código."); var old = new { x.Code, x.Name, x.Type, x.Value, x.IsActive }; Assign(x, r, type); x.UpdatedAtUtc = DateTime.UtcNow; AuditTrail.Add(Db, Audit, "CouponUpdated", "Coupon", x.Id, old, new { x.Code, x.Name, x.Type, x.Value, x.IsActive }); await Db.SaveChangesAsync(ct); return AppResult<CouponResponse>.Success(CouponRules.Map(x)); }
}
public class UpdateCouponStatusUseCase(IApplicationDbContext db, IAuditContext? audit = null) : IUpdateCouponStatusUseCase { public async Task<AppResult> ExecuteAsync(int id, UpdateCouponStatusRequest r, CancellationToken ct = default) { var x = await db.Coupons.SingleOrDefaultAsync(c => c.Id == id, ct); if (x is null) return AppResult.Failure(AppErrorType.NotFound, "Cupón no encontrado."); if (x.IsActive == r.IsActive) return AppResult.Success(); var old = x.IsActive; x.IsActive = r.IsActive; x.UpdatedAtUtc = DateTime.UtcNow; AuditTrail.Add(db, audit, "CouponStatusChanged", "Coupon", id, new { IsActive = old }, new { x.IsActive }); await db.SaveChangesAsync(ct); return AppResult.Success(); } }

public class ApplyCartCouponUseCase(IApplicationDbContext db) : IApplyCartCouponUseCase
{
    public async Task<AppResult> ExecuteAsync(int userId, ApplyCouponRequest r, CancellationToken ct = default) { var code = CouponRules.NormalizeCode(r.Code); if (code.Length == 0) return AppResult.Failure(AppErrorType.Validation, "Ingresá un código de descuento."); var cart = await db.Carts.Include(x => x.Items).ThenInclude(x => x.Product).SingleOrDefaultAsync(x => x.UserId == userId, ct); if (cart is null || cart.Items.Count == 0) return AppResult.Failure(AppErrorType.Validation, "El carrito está vacío."); var coupon = await db.Coupons.SingleOrDefaultAsync(x => x.Code == code, ct); if (coupon is null) return AppResult.Failure(AppErrorType.NotFound, "El código de descuento no es válido."); var subtotal = cart.Items.Sum(x => x.Product.Price * x.Quantity); var active = await db.CouponRedemptions.CountAsync(x => x.CouponId == coupon.Id && x.Status != CouponRedemptionStatus.Released, ct); var own = await db.CouponRedemptions.CountAsync(x => x.CouponId == coupon.Id && x.UserId == userId && x.Status != CouponRedemptionStatus.Released, ct); var error = CouponRules.ValidateAvailability(coupon, subtotal, DateTime.UtcNow, active, own); if (error is not null) return AppResult.Failure(AppErrorType.Validation, error); cart.CouponId = coupon.Id; cart.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); return AppResult.Success(); }
}
public class RemoveCartCouponUseCase(IApplicationDbContext db) : IRemoveCartCouponUseCase { public async Task<AppResult> ExecuteAsync(int userId, CancellationToken ct = default) { var cart = await db.Carts.SingleOrDefaultAsync(x => x.UserId == userId, ct); if (cart is null) return AppResult.Success(); cart.CouponId = null; cart.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); return AppResult.Success(); } }

