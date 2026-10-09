using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GymShop.Application.UseCases.Orders;

public interface IGetMyOrdersUseCase
{
    Task<List<OrderSummaryResponse>> ExecuteAsync(int userId, CancellationToken cancellationToken = default);
}

public interface IGetOrderByIdUseCase
{
    Task<AppResult<OrderResponse>> ExecuteAsync(int id, int userId, bool canViewAll, CancellationToken cancellationToken = default);
}

public interface IGetOrdersUseCase
{
    Task<AppResult<PagedOrdersResponse>> ExecuteAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default);
}

public interface ICancelOrderUseCase
{
    Task<AppResult<OrderResponse>> ExecuteAsync(int id, int userId, bool canManageAll, CancelOrderRequest request, CancellationToken cancellationToken = default);
}

public interface IExpirePendingOrdersUseCase
{
    Task<AppResult<ExpirePendingOrdersResponse>> ExecuteAsync(ExpirePendingOrdersRequest request, CancellationToken cancellationToken = default);
}

public interface IUpdateOrderStatusUseCase
{
    Task<AppResult> ExecuteAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default);
}

public interface IGetOrderHistoryUseCase
{
    Task<AppResult<List<OrderHistoryEventResponse>>> ExecuteAsync(int orderId, CancellationToken cancellationToken = default);
}

public class GetMyOrdersUseCase : IGetMyOrdersUseCase
{
    private readonly IApplicationDbContext _db;

    public GetMyOrdersUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<OrderSummaryResponse>> ExecuteAsync(int userId, CancellationToken cancellationToken = default)
    {
        return await _db.Orders
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Include(x => x.User)
            .Include(x => x.Payments)
            .Include(x => x.CouponRedemption)
            .OrderByDescending(x => x.Id)
            .Select(x => OrderMapper.ToSummaryResponse(x, x.User!.Email))
            .ToListAsync(cancellationToken);
    }
}

public class GetOrderByIdUseCase : IGetOrderByIdUseCase
{
    private readonly IApplicationDbContext _db;

    public GetOrderByIdUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<OrderResponse>> ExecuteAsync(int id, int userId, bool canViewAll, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.CouponRedemption)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        }

        if (order.UserId != userId && !canViewAll)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.Forbidden, "No tenes permisos para ver este pedido.");
        }

        return AppResult<OrderResponse>.Success(OrderMapper.ToResponse(order));
    }
}

public class GetOrdersUseCase : IGetOrdersUseCase
{
    private readonly IApplicationDbContext _db;

    public GetOrdersUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<PagedOrdersResponse>> ExecuteAsync(OrderFilterRequest filter, CancellationToken cancellationToken = default)
    {
        if (filter.Page < 1 || filter.PageSize is < 1 or > 100)
            return AppResult<PagedOrdersResponse>.Failure(AppErrorType.Validation, "La paginacion solicitada no es valida.");
        if (filter.FromUtc > filter.ToUtc)
            return AppResult<PagedOrdersResponse>.Failure(AppErrorType.Validation, "El rango de fechas no es valido.");

        OrderStatus? status = null;
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<OrderStatus>(filter.Status, true, out var parsed) || !Enum.IsDefined(parsed))
                return AppResult<PagedOrdersResponse>.Failure(AppErrorType.Validation, "Estado de pedido invalido.");
            status = parsed;
        }

        var query = _db.Orders
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim().ToLower();
            var isOrderNumber = int.TryParse(search.TrimStart('#'), out var orderId);
            query = query.Where(x =>
                (isOrderNumber && x.Id == orderId) ||
                (x.User != null && (x.User.Email.ToLower().Contains(search) ||
                    (x.User.Name + " " + (x.User.LastName ?? "")).ToLower().Contains(search))) ||
                ((x.GuestEmail ?? "").ToLower().Contains(search)) ||
                (((x.GuestFirstName ?? "") + " " + (x.GuestLastName ?? "")).ToLower().Contains(search)));
        }
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (filter.FromUtc.HasValue) query = query.Where(x => x.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue) query = query.Where(x => x.CreatedAt <= filter.ToUtc.Value);

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new OrderSummaryResponse(
                x.Id, x.UserId, x.User != null ? x.User.Email : x.GuestEmail ?? string.Empty,
                x.User != null
                    ? (x.User.Name + " " + (x.User.LastName ?? "")).Trim()
                    : ((x.GuestFirstName ?? "") + " " + (x.GuestLastName ?? "")).Trim(),
                x.CreatedAt, x.Total, x.DeliveryMethod.ToString(), x.Status.ToString(), x.UpdatedAt,
                x.Payments.OrderByDescending(payment => payment.Id).Select(payment => payment.Status.ToString()).FirstOrDefault(),
                x.Payments.OrderByDescending(payment => payment.Id).Select(payment => (int?)payment.Id).FirstOrDefault()))
            .ToListAsync(cancellationToken);
        var pages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)filter.PageSize);
        return AppResult<PagedOrdersResponse>.Success(new PagedOrdersResponse(items, filter.Page, filter.PageSize, total, pages));
    }
}

public sealed class GetOrderHistoryUseCase : IGetOrderHistoryUseCase
{
    private static readonly string[] OrderActions = ["OrderStatusChanged", "OrderTrackingUpdated", "OrderCanceled", "OrderExpiredAdministratively", "PaymentWebhookUnmatched", "FreeOrderConfirmed", "PurchaseReceiptCreated", "ArcaHomologationInvoiceRequested", "ArcaHomologationInvoiceAuthorized", "ArcaHomologationInvoiceRejected", "ArcaHomologationInvoiceRecovered", "ArcaHomologationCreditNoteRequested", "ArcaHomologationCreditNoteAuthorized", "ArcaHomologationCreditNoteRejected", "ArcaHomologationCreditNoteRecovered"];
    private static readonly string[] PaymentActions = ["PaymentResolvedByProvider", "PaymentResolvedManually", "PaymentRefundedByProvider", "PaymentPartialRefundFlagged", "PaymentApprovedAfterOrderCancellation", "PaymentPreferenceInvalidationFailed"];
    private readonly IApplicationDbContext _db;

    public GetOrderHistoryUseCase(IApplicationDbContext db) => _db = db;

    public async Task<AppResult<List<OrderHistoryEventResponse>>> ExecuteAsync(int orderId, CancellationToken cancellationToken = default)
    {
        if (!await _db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId, cancellationToken))
            return AppResult<List<OrderHistoryEventResponse>>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");

        var orderEntityId = orderId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var paymentIds = (await _db.Payments.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken))
            .Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        var rows = await _db.AuditEntries.AsNoTracking()
            .Where(x =>
                (x.EntityType == "Order" && x.EntityId == orderEntityId && OrderActions.Contains(x.Action)) ||
                (x.EntityType == "Payment" && paymentIds.Contains(x.EntityId) && PaymentActions.Contains(x.Action)))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id, x.Action, x.OldValue, x.NewValue, x.Reason, x.CreatedAtUtc, x.ActorUserId,
                ActorName = x.ActorUser == null ? null : (x.ActorUser.Name + " " + (x.ActorUser.LastName ?? "")).Trim(),
                ActorEmail = x.ActorUser == null ? null : x.ActorUser.Email
            })
            .ToListAsync(cancellationToken);

        var events = rows.Select(x => new OrderHistoryEventResponse(
            x.Id,
            x.Action,
            ReadStatus(x.OldValue),
            ReadStatus(x.NewValue),
            x.Reason,
            x.CreatedAtUtc,
            x.ActorUserId,
            x.ActorName,
            x.ActorEmail,
            x.Action.Contains("ByProvider", StringComparison.Ordinal) || x.Action is "PaymentRefundedByProvider" or "PaymentPartialRefundFlagged"
                ? "Provider"
                : x.ActorUserId.HasValue ? "Manual" : "Automatic"))
            .ToList();

        return AppResult<List<OrderHistoryEventResponse>>.Success(events);
    }

    private static string? ReadStatus(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var property in new[] { "orderStatus", "status" })
                if (document.RootElement.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
        }
        catch (JsonException)
        {
            // Historical audit data may be malformed; keep the event without status details.
        }
        return null;
    }
}


public class CancelOrderUseCase : ICancelOrderUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;
    private readonly ITransactionManager? _transactionManager;

    public CancelOrderUseCase(IApplicationDbContext db, IAuditContext? auditContext = null, ITransactionManager? transactionManager = null)
    {
        _db = db;
        _auditContext = auditContext;
        _transactionManager = transactionManager;
    }

    public async Task<AppResult<OrderResponse>> ExecuteAsync(int id, int userId, bool canManageAll, CancelOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Reason?.Trim().Length > ValidationLimits.CancellationReason)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.Validation, "El motivo no puede superar 500 caracteres.");
        }

        await using var transaction = _transactionManager is null
            ? null
            : await _transactionManager.BeginOrderCancellationTransactionAsync(id, cancellationToken);

        var order = await _db.Orders
            .Include(x => x.User)
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.ProductVariant)
            .Include(x => x.Payments)
            .Include(x => x.CouponRedemption)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (order is null)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        }

        if (!canManageAll)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.Forbidden, "Solo un administrador puede cancelar pedidos.");
        }

        if (order.Status == OrderStatus.Canceled)
        {
            return AppResult<OrderResponse>.Success(OrderMapper.ToResponse(order));
        }

        var isCancelableFreeOrder = order.Total == 0 &&
                                    order.Status is OrderStatus.Paid or OrderStatus.Preparing &&
                                    !order.Payments.Any();
        if (order.Status != OrderStatus.Pending && !isCancelableFreeOrder)
        {
            return AppResult<OrderResponse>.Failure(AppErrorType.Conflict, "Solo se pueden cancelar pedidos pendientes o pedidos gratuitos confirmados que todavía no fueron enviados.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
            return AppResult<OrderResponse>.Failure(AppErrorType.Validation, "El motivo de cancelacion es obligatorio.");

        var reason = request.Reason.Trim();
        var previousStatus = order.Status;
        if (isCancelableFreeOrder)
            OrderCompensation.CancelFreeOrderAndRestoreStock(_db, order, reason, _auditContext?.ActorUserId);
        else
            OrderCompensation.CancelPendingAndRestoreStock(_db, order, reason, _auditContext?.ActorUserId);
        AuditTrail.Add(_db, _auditContext, "OrderCanceled", "Order", order.Id,
            new { status = previousStatus.ToString() }, new { status = order.Status.ToString() }, reason);
        await _db.SaveChangesAsync(cancellationToken);

        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return AppResult<OrderResponse>.Success(OrderMapper.ToResponse(order));
    }
}

public class ExpirePendingOrdersUseCase : IExpirePendingOrdersUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;

    public ExpirePendingOrdersUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        _db = db;
        _auditContext = auditContext;
    }

    public async Task<AppResult<ExpirePendingOrdersResponse>> ExecuteAsync(ExpirePendingOrdersRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OlderThanMinutes <= 0)
        {
            return AppResult<ExpirePendingOrdersResponse>.Failure(AppErrorType.Validation, "El vencimiento debe ser mayor a cero minutos.");
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-request.OlderThanMinutes);
        var orders = await _db.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.ProductVariant)
            .Include(x => x.Payments)
            .Include(x => x.CouponRedemption)
            .Where(x => x.Status == OrderStatus.Pending && x.CreatedAt <= cutoff &&
                        !x.Payments.Any(payment => payment.Provider == "MercadoPago" &&
                            (payment.Status == PaymentStatus.Creating || payment.Status == PaymentStatus.Pending)))
            .ToListAsync(cancellationToken);

        foreach (var order in orders)
        {
            OrderCompensation.CancelPendingAndRestoreStock(_db, order, "Pedido pendiente expirado.");
            AuditTrail.Add(_db, _auditContext, "OrderExpiredAdministratively", "Order", order.Id,
                new { status = OrderStatus.Pending.ToString() }, new { status = order.Status.ToString() },
                $"OlderThanMinutes={request.OlderThanMinutes}");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return AppResult<ExpirePendingOrdersResponse>.Success(new ExpirePendingOrdersResponse(orders.Count));
    }
}

internal static class OrderCompensation
{
    public static bool CancelFreeOrderAndRestoreStock(IApplicationDbContext db, Order order, string reason, int? actorUserId = null)
    {
        if (order.Total != 0 || order.Status is not (OrderStatus.Paid or OrderStatus.Preparing) ||
            order.Payments.Any())
        {
            return false;
        }

        if (order.StockReserved) RestoreStock(db, order, reason, actorUserId);
        order.Status = OrderStatus.Canceled;
        order.CancellationReason ??= reason;
        order.UpdatedAt = DateTime.UtcNow;
        CouponRedemptionLifecycle.ReleaseConsumedAfterCancellation(order);
        return true;
    }

    public static bool CancelPendingAndRestoreStock(IApplicationDbContext db, Order order, string reason, int? actorUserId = null)
    {
        if (order.Status != OrderStatus.Pending)
        {
            return false;
        }

        order.Status = OrderStatus.Canceled;
        order.CancellationReason ??= reason;
        order.UpdatedAt = DateTime.UtcNow;

        foreach (var payment in order.Payments.Where(x =>
                     x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending))
        {
            payment.Status = PaymentStatus.Canceled;
            payment.FailureReason = reason;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        if (order.StockReserved) RestoreStock(db, order, reason, actorUserId);

        CouponRedemptionLifecycle.Release(order);

        return true;
    }

    private static void RestoreStock(IApplicationDbContext db, Order order, string reason, int? actorUserId)
    {
        foreach (var item in order.Items)
        {
            var previousStock = item.ProductVariant?.Stock ?? item.Product.Stock;
            if (item.ProductVariant is not null) item.ProductVariant.Stock += item.Quantity;
            else item.Product.Stock += item.Quantity;
            item.Product.UpdatedAt = DateTime.UtcNow;
            StockMovementRecorder.Add(db, item.Product, StockMovementType.CancellationReturn, item.Quantity,
                previousStock, reason, actorUserId, order, item.ProductVariant);
        }
    }

    public static bool ApplyConfirmedRefund(IApplicationDbContext db, Order order, string reason, int? actorUserId = null)
    {
        if (order.Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered))
        {
            return false;
        }

        var restoreStock = order.Status is OrderStatus.Paid or OrderStatus.Preparing;
        order.Status = OrderStatus.Refunded;
        order.UpdatedAt = DateTime.UtcNow;

        if (restoreStock)
        {
            foreach (var item in order.Items)
            {
                var previousStock = item.ProductVariant?.Stock ?? item.Product.Stock;
                if (item.ProductVariant is not null) item.ProductVariant.Stock += item.Quantity;
                else item.Product.Stock += item.Quantity;
                item.Product.UpdatedAt = DateTime.UtcNow;
                StockMovementRecorder.Add(db, item.Product, StockMovementType.CancellationReturn, item.Quantity,
                    previousStock, reason, actorUserId, order, item.ProductVariant);
            }
        }

        return true;
    }
}

public class UpdateOrderStatusUseCase : IUpdateOrderStatusUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;

    public UpdateOrderStatusUseCase(IApplicationDbContext db, IAuditContext? auditContext = null)
    {
        _db = db;
        _auditContext = auditContext;
    }

    public async Task<AppResult> ExecuteAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<OrderStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
        {
            return AppResult.Failure(AppErrorType.Validation, "Estado invalido.");
        }

        var order = await _db.Orders
            .Include(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.ProductVariant)
            .Include(x => x.Payments)
            .Include(x => x.CouponRedemption)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (order is null)
        {
            return AppResult.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        }

        if (!OrderStatusTransitions.CanAdminMove(order.Status, status))
        {
            return AppResult.Failure(AppErrorType.Conflict, "Transicion de estado invalida. Los pagos y reembolsos deben resolverse desde su flujo especifico.");
        }

        if (status == OrderStatus.Canceled)
            return AppResult.Failure(AppErrorType.Validation, "Usa el flujo de cancelacion con motivo obligatorio.");

        var trackingResult = ValidateTracking(order, status, request);
        if (trackingResult is not null) return trackingResult;

        if (order.Status == status && status != OrderStatus.Shipped)
            return AppResult.Success();

        var oldStatus = order.Status;
        if (request.ExpectedUpdatedAt.HasValue && order.UpdatedAt != request.ExpectedUpdatedAt.Value)
            return AppResult.Failure(AppErrorType.Conflict, "El pedido fue actualizado por otro usuario. Recarga el detalle antes de continuar.");
        if (status == OrderStatus.Shipped)
        {
            var oldTracking = new { order.Carrier, order.TrackingNumber, order.TrackingUrl };
            order.Carrier = Normalize(request.Carrier);
            order.TrackingNumber = Normalize(request.TrackingNumber);
            order.TrackingUrl = Normalize(request.TrackingUrl);
            order.Status = status;
            order.UpdatedAt = DateTime.UtcNow;
            if (oldStatus == status)
                AuditTrail.Add(_db, _auditContext, "OrderTrackingUpdated", "Order", order.Id, oldTracking,
                    new { order.Carrier, order.TrackingNumber, order.TrackingUrl });
        }
        else if (status == OrderStatus.Canceled)
        {
            OrderCompensation.CancelPendingAndRestoreStock(_db, order, "Cancelacion administrativa del pedido.", _auditContext?.ActorUserId);
        }
        else
        {
            order.Status = status;
            order.UpdatedAt = DateTime.UtcNow;
        }
        if (oldStatus != status)
            AuditTrail.Add(_db, _auditContext, "OrderStatusChanged", "Order", order.Id,
                new { status = oldStatus.ToString() },
                new { status = order.Status.ToString() },
                status == OrderStatus.Canceled ? "Cancelacion administrativa del pedido." : null);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AppResult.Failure(AppErrorType.Conflict, "El pedido fue actualizado por otro usuario. Recarga el detalle antes de continuar.");
        }

        return AppResult.Success();
    }

    private static AppResult? ValidateTracking(Order order, OrderStatus status, UpdateOrderStatusRequest request)
    {
        if (status != OrderStatus.Shipped) return null;
        if (order.DeliveryMethod == DeliveryMethod.HomeDelivery &&
            (string.IsNullOrWhiteSpace(request.Carrier) || string.IsNullOrWhiteSpace(request.TrackingNumber)))
            return AppResult.Failure(AppErrorType.Validation, "La empresa transportista y el numero de seguimiento son obligatorios para un envio a domicilio.");
        if (request.Carrier?.Trim().Length > ValidationLimits.Carrier || request.TrackingNumber?.Trim().Length > ValidationLimits.TrackingNumber)
            return AppResult.Failure(AppErrorType.Validation, "Los datos de seguimiento superan la longitud permitida.");
        if (!string.IsNullOrWhiteSpace(request.TrackingUrl) &&
            (!Uri.TryCreate(request.TrackingUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            return AppResult.Failure(AppErrorType.Validation, "La URL de seguimiento debe ser una URL HTTPS valida.");
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal static class OrderStatusTransitions
{
    public static bool CanAdminMove(OrderStatus current, OrderStatus next)
    {
        if (current == next)
        {
            return true;
        }

        return (current, next) switch
        {
            (OrderStatus.Pending, OrderStatus.Canceled) => true,
            (OrderStatus.Paid, OrderStatus.Preparing) => true,
            (OrderStatus.Preparing, OrderStatus.Shipped) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered) => true,
            _ => false
        };
    }
}

internal static class OrderQueries
{
    public static async Task<OrderResponse> LoadOrderResponseAsync(IApplicationDbContext db, int id, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return OrderMapper.ToResponse(order);
    }
}

internal static class OrderMapper
{
    public static OrderResponse ToResponse(Order order)
    {
        return new OrderResponse(
            order.Id,
            order.UserId,
            order.User?.Email ?? order.GuestEmail,
            order.User is null ? $"{order.GuestFirstName} {order.GuestLastName}".Trim() : $"{order.User.Name} {order.User.LastName}".Trim(),
            order.CreatedAt,
            order.Subtotal,
            order.CouponCode,
            order.DiscountAmount,
            order.DeliveryMethod.ToString(),
            order.ShippingCost,
            order.Total,
            order.Status.ToString(),
            order.ShippingAddress,
            order.PickupAddress,
            order.PickupHours,
            order.PickupInstructions,
            order.Carrier,
            order.TrackingNumber,
            order.TrackingUrl,
            order.CancellationReason,
            order.UpdatedAt,
            order.Items
                .OrderBy(x => x.Id)
                .Select(x => new OrderItemResponse(x.ProductId, x.ProductName, x.UnitPrice, x.Quantity, x.Subtotal,
                    x.ProductVariantId, x.VariantSku, string.IsNullOrWhiteSpace(x.VariantAttributesJson) ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(x.VariantAttributesJson)))
                .ToList(),
            order.Payments
                .OrderByDescending(x => x.Id)
                .Select(x => new OrderPaymentResponse(x.Id, x.Provider, x.ExternalReference, x.Amount, x.Currency, x.Status.ToString(), x.CreatedAt, x.PaidAt,
                    x.FailureReason,
                    order.Status == OrderStatus.Canceled && x.Status == PaymentStatus.Approved))
                .ToList(),
            string.IsNullOrWhiteSpace(order.ShippingPostalCode) ? null : new OrderShippingAddressResponse(
                order.ShippingPostalCode,
                order.ShippingProvince ?? string.Empty,
                order.ShippingCity ?? string.Empty,
                order.ShippingStreet ?? string.Empty,
                order.ShippingStreetNumber ?? string.Empty,
                order.ShippingFloor,
                order.ShippingApartment,
                order.ShippingNotes),
            order.ShippingProviderCode,
            order.ShippingServiceCode,
            order.ShippingServiceName,
            order.GuestPhone,
            order.ExpiresAtUtc
        );
    }

    public static OrderSummaryResponse ToSummaryResponse(Order order, string? userEmail)
    {
        var lastPayment = order.Payments.OrderByDescending(x => x.Id).FirstOrDefault();

        return new OrderSummaryResponse(
            order.Id,
            order.UserId,
            userEmail,
            order.User is null ? $"{order.GuestFirstName} {order.GuestLastName}".Trim() : $"{order.User.Name} {order.User.LastName}".Trim(),
            order.CreatedAt,
            order.Total,
            order.DeliveryMethod.ToString(),
            order.Status.ToString(),
            order.UpdatedAt,
            lastPayment?.Status.ToString(),
            lastPayment?.Id
        );
    }
}










