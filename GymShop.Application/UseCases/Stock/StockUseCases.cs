using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Stock;

public interface IGetStockMovementsUseCase
{
    Task<AppResult<PagedStockMovementsResponse>> ExecuteAsync(StockMovementQuery request, CancellationToken cancellationToken = default);
}

public interface IAdjustStockUseCase
{
    Task<AppResult<StockAdjustmentResponse>> ExecuteAsync(int productId, ManualStockAdjustmentRequest request, CancellationToken cancellationToken = default);
}

public sealed class GetStockMovementsUseCase(IApplicationDbContext db) : IGetStockMovementsUseCase
{
    public async Task<AppResult<PagedStockMovementsResponse>> ExecuteAsync(StockMovementQuery request, CancellationToken cancellationToken = default)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            return AppResult<PagedStockMovementsResponse>.Failure(AppErrorType.Validation, "La paginacion solicitada no es valida.");
        if (request.FromUtc > request.ToUtc)
            return AppResult<PagedStockMovementsResponse>.Failure(AppErrorType.Validation, "El rango de fechas no es valido.");

        StockMovementType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!Enum.TryParse<StockMovementType>(request.Type, true, out var parsed) || !Enum.IsDefined(parsed))
                return AppResult<PagedStockMovementsResponse>.Failure(AppErrorType.Validation, "Tipo de movimiento invalido.");
            type = parsed;
        }

        var query = db.StockMovements.AsNoTracking().AsQueryable();
        if (request.ProductId.HasValue) query = query.Where(x => x.ProductId == request.ProductId);
        if (type.HasValue) query = query.Where(x => x.Type == type);
        if (request.OrderId.HasValue) query = query.Where(x => x.OrderId == request.OrderId);
        if (request.ActorUserId.HasValue) query = query.Where(x => x.ActorUserId == request.ActorUserId);
        if (request.FromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc <= request.ToUtc);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(x => new StockMovementResponse(x.Id, x.ProductId, x.Product.Name, x.Type.ToString(),
                x.Quantity, x.PreviousStock, x.ResultingStock, x.Reason, x.ActorUserId,
                x.ActorUser == null ? null : x.ActorUser.Name, x.OrderId, x.CreatedAtUtc, x.ProductVariantId, x.ProductVariant == null ? null : x.ProductVariant.Sku))
            .ToListAsync(cancellationToken);
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)request.PageSize);
        return AppResult<PagedStockMovementsResponse>.Success(new(items, request.Page, request.PageSize, total, pages));
    }
}

public sealed class AdjustStockUseCase : IAdjustStockUseCase
{
    private readonly IApplicationDbContext db;
    private readonly IAuditContext auditContext;

    public AdjustStockUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        this.db = db;
        this.auditContext = auditContext ?? SystemAuditContext.Instance;
    }

    public async Task<AppResult<StockAdjustmentResponse>> ExecuteAsync(int productId, ManualStockAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Quantity == 0)
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "La cantidad debe ser distinta de cero.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "El motivo es obligatorio.");
        if (request.Reason.Trim().Length > 500)
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "El motivo no puede superar 500 caracteres.");
        const StockMovementType type = StockMovementType.ManualCorrection;

        var product = await db.Products.Include(x => x.Variants).SingleOrDefaultAsync(x => x.Id == productId, cancellationToken);
        if (product is null)
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.NotFound, "Producto no encontrado.");

        ProductVariant? variant = null;
        if (product.Variants.Count > 0)
        {
            if (!request.ProductVariantId.HasValue)
                return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "Seleccioná una variante para ajustar su stock.");
            variant = product.Variants.SingleOrDefault(x => x.Id == request.ProductVariantId && x.IsActive);
            if (variant is null)
                return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "La variante indicada no pertenece al producto o está inactiva.");
        }
        else if (request.ProductVariantId.HasValue)
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "Un producto simple no admite una variante de stock.");

        var delta = request.Quantity;
        var previous = variant?.Stock ?? product.Stock;
        var resulting = previous + delta;
        if (resulting < 0)
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Validation, "El ajuste no puede dejar el stock en negativo.");

        if (variant is null) product.Stock = resulting; else variant.Stock = resulting;
        product.UpdatedAt = DateTime.UtcNow;
        var movement = StockMovementRecorder.Add(db, product, type, delta, previous, request.Reason.Trim(), auditContext.ActorUserId, variant: variant);
        AuditTrail.Add(db, auditContext, "ProductStockAdjusted", "Product", product.Id,
            new { stock = previous, variantId = variant?.Id }, new { stock = resulting, variantId = variant?.Id, type = type.ToString(), quantity = delta }, request.Reason.Trim());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppResult<StockAdjustmentResponse>.Failure(AppErrorType.Conflict, "El stock fue modificado por otra operacion. Recarga y volve a intentar.");
        }

        var response = new StockMovementResponse(movement.Id, product.Id, product.Name, type.ToString(), delta,
            previous, resulting, movement.Reason, movement.ActorUserId, null, null, movement.CreatedAtUtc, variant?.Id, variant?.Sku);
        return AppResult<StockAdjustmentResponse>.Success(new(product.Id, previous, resulting, response));
    }
}

public static class StockMovementRecorder
{
    public static StockMovement Add(IApplicationDbContext db, Product product, StockMovementType type, int quantity,
        int previousStock, string reason, int? actorUserId = null, Order? order = null, ProductVariant? variant = null)
    {
        var movement = new StockMovement
        {
            Product = product, ProductId = product.Id, ProductVariant = variant, ProductVariantId = variant?.Id, Type = type, Quantity = quantity,
            PreviousStock = previousStock, ResultingStock = previousStock + quantity,
            Reason = reason, ActorUserId = actorUserId, Order = order, OrderId = order?.Id > 0 ? order.Id : null
        };
        db.StockMovements.Add(movement);
        return movement;
    }
}
