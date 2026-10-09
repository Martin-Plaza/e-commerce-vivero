using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.UseCases.Carts;
using GymShop.Application.UseCases.Orders;
using GymShop.Application.UseCases.Stock;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GymShop.Application.UseCases.Payments;

public interface ICreatePaymentUseCase
{
    Task<AppResult<PaymentResponse>> ExecuteAsync(int orderId, int userId, bool canManageAll, CreatePaymentRequest request, CancellationToken cancellationToken = default);
}

public interface ICreateCheckoutPaymentUseCase
{
    Task<AppResult<PaymentResponse>> ExecuteAsync(int checkoutId, int userId, CreatePaymentRequest request, CancellationToken cancellationToken = default);
}

public interface IGetPaymentByIdUseCase
{
    Task<AppResult<PaymentResponse>> ExecuteAsync(int id, int userId, bool canManageAll, CancellationToken cancellationToken = default);
}

public interface IGetOrderPaymentsUseCase
{
    Task<AppResult<List<PaymentResponse>>> ExecuteAsync(int orderId, int userId, bool canManageAll, CancellationToken cancellationToken = default);
}

public interface IUpdatePaymentStatusUseCase
{
    Task<AppResult<PaymentResponse>> ExecuteAsync(int id, UpdatePaymentStatusRequest request, CancellationToken cancellationToken = default);
}

public interface IHandlePaymentWebhookUseCase
{
    Task<AppResult<PaymentResponse>> ExecuteAsync(string provider, string providerPaymentId, CancellationToken cancellationToken = default);
}

public sealed class PaymentCreationPolicy
{
    public static PaymentCreationPolicy Default { get; } = FromSeconds(300);

    private PaymentCreationPolicy(TimeSpan creatingTimeout)
    {
        CreatingTimeout = creatingTimeout;
    }

    public TimeSpan CreatingTimeout { get; }

    public static PaymentCreationPolicy FromSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "The payment creation timeout must be greater than zero.");
        }

        return new PaymentCreationPolicy(TimeSpan.FromSeconds(seconds));
    }
}

public class CreatePaymentUseCase : ICreatePaymentUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<IPaymentGateway> _gateways;
    private readonly PaymentCreationPolicy _policy;

    public CreatePaymentUseCase(IApplicationDbContext db, IEnumerable<IPaymentGateway> gateways)
        : this(db, gateways, PaymentCreationPolicy.Default)
    {
    }

    public CreatePaymentUseCase(
        IApplicationDbContext db,
        IEnumerable<IPaymentGateway> gateways,
        PaymentCreationPolicy policy)
    {
        _db = db;
        _gateways = gateways;
        _policy = policy;
    }

    public async Task<AppResult<PaymentResponse>> ExecuteAsync(int orderId, int userId, bool canManageAll, CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders
            .Include(x => x.User)
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

        if (order is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        }

        if (order.UserId != userId && !canManageAll)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Forbidden, "No tenes permisos para pagar este pedido.");
        }

        if (order.Status == OrderStatus.Paid)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pedido ya esta pagado.");
        }

        if (order.Total <= 0)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pedido no requiere un pago externo.");
        }

        if (order.Status != OrderStatus.Pending)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pedido no admite nuevos pagos.");
        }

        return await PaymentCreator.CreateAsync(_db, _gateways, order, request, _policy, cancellationToken);
    }
}

internal static class PaymentCreator
{
    public static async Task<AppResult<PaymentResponse>> CreateAsync(
        IApplicationDbContext db,
        IEnumerable<IPaymentGateway> gateways,
        Order order,
        CreatePaymentRequest request,
        PaymentCreationPolicy policy,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"server-{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > ValidationLimits.IdempotencyKey)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia no puede superar 100 caracteres.");
        }

        var idempotentPayment = await db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (idempotentPayment is not null)
        {
            if (idempotentPayment.OrderId != order.Id)
            {
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada en otra orden.");
            }

            return await ResumeOrReuseAsync(db, gateways, order, idempotentPayment, policy, cancellationToken);
        }

        var activePayment = await db.Payments
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id &&
                        (x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending))
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (activePayment is not null)
        {
            return await ResumeOrReuseAsync(db, gateways, order, activePayment, policy, cancellationToken);
        }

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "Mock" : request.Provider.Trim();
        if (provider.Length > ValidationLimits.PaymentProvider)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "El proveedor no puede superar 50 caracteres.");
        }

        var gateway = gateways.FirstOrDefault(x => x.CanHandle(provider));
        if (gateway is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, $"Proveedor de pago no soportado: {provider}.");
        }

        var now = DateTime.UtcNow;
        var reservation = new Payment
        {
            OrderId = order.Id,
            Provider = provider,
            ExternalReference = $"order-{order.Id}",
            IdempotencyKey = idempotencyKey,
            Amount = order.Total,
            Currency = "ARS",
            Status = PaymentStatus.Creating,
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Payments.Add(reservation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Payments.Remove(reservation);

            var winner = await db.Payments
                .AsNoTracking()
                .Where(x => x.IdempotencyKey == idempotencyKey ||
                            (x.OrderId == order.Id &&
                             (x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending)))
                .OrderByDescending(x => x.IdempotencyKey == idempotencyKey)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (winner is null)
            {
                throw;
            }

            if (winner.IdempotencyKey == idempotencyKey && winner.OrderId != order.Id)
            {
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada en otra orden.");
            }

            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(winner));
        }

        return await CompleteReservationAsync(db, gateway, order, reservation, cancellationToken);
    }

    private static async Task<AppResult<PaymentResponse>> ResumeOrReuseAsync(
        IApplicationDbContext db,
        IEnumerable<IPaymentGateway> gateways,
        Order order,
        Payment payment,
        PaymentCreationPolicy policy,
        CancellationToken cancellationToken)
    {
        if (payment.Status != PaymentStatus.Creating)
        {
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        var lastActivity = payment.UpdatedAt ?? payment.CreatedAt;
        var staleBefore = DateTime.UtcNow.Subtract(policy.CreatingTimeout);
        if (lastActivity > staleBefore)
        {
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        var claimedAt = DateTime.UtcNow;
        var claimed = await db.Payments
            .Where(x => x.Id == payment.Id &&
                        x.Status == PaymentStatus.Creating &&
                        (x.UpdatedAt ?? x.CreatedAt) <= staleBefore)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.UpdatedAt, claimedAt),
                cancellationToken);

        if (claimed == 0)
        {
            var current = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == payment.Id, cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(current));
        }

        var reservation = await db.Payments.SingleAsync(x => x.Id == payment.Id, cancellationToken);
        var gateway = gateways.FirstOrDefault(x => x.CanHandle(reservation.Provider));
        if (gateway is null)
        {
            reservation.Status = PaymentStatus.CreationFailed;
            reservation.FailureReason = $"Proveedor de pago no soportado: {reservation.Provider}.";
            reservation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, reservation.FailureReason);
        }

        return await CompleteReservationAsync(db, gateway, order, reservation, cancellationToken);
    }

    private static async Task<AppResult<PaymentResponse>> CompleteReservationAsync(
        IApplicationDbContext db,
        IPaymentGateway gateway,
        Order order,
        Payment reservation,
        CancellationToken cancellationToken)
    {
        reservation.ExternalReference = string.Equals(reservation.Provider, "BankTransfer", StringComparison.OrdinalIgnoreCase)
            ? await BankTransferReference.CreateUniqueAsync(db, cancellationToken)
            : PaymentExternalReferences.Build(order.Id, reservation.Id);
        await db.SaveChangesAsync(cancellationToken);
        PaymentPreferenceResult preference;
        try
        {
            preference = await gateway.CreatePreferenceAsync(order, reservation.IdempotencyKey, reservation.ExternalReference, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            var stillCreating = await IsReservationStillCreatingForPendingOrderAsync(db, reservation.Id, cancellationToken);
            if (!stillCreating)
            {
                var current = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == reservation.Id, cancellationToken);
                return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(current));
            }

            reservation.Status = PaymentStatus.CreationFailed;
            reservation.FailureReason = ex.Message;
            reservation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, ex.Message);
        }

        var canComplete = await IsReservationStillCreatingForPendingOrderAsync(db, reservation.Id, cancellationToken);
        if (!canComplete)
        {
            var current = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == reservation.Id, cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(current));
        }

        reservation.Provider = preference.Provider;
        reservation.ProviderPreferenceId = preference.ProviderPreferenceId;
        reservation.Status = PaymentStatus.Pending;
        reservation.CheckoutUrl = preference.CheckoutUrl;
        reservation.FailureReason = null;
        reservation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(reservation));
    }

    private static Task<bool> IsReservationStillCreatingForPendingOrderAsync(
        IApplicationDbContext db,
        int paymentId,
        CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().AnyAsync(
            x => x.Id == paymentId &&
                 x.Status == PaymentStatus.Creating &&
                 x.Order != null &&
                 x.Order.Status == OrderStatus.Pending,
            cancellationToken);
}

public sealed class CreateCheckoutPaymentUseCase : ICreateCheckoutPaymentUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<IPaymentGateway> _gateways;

    public CreateCheckoutPaymentUseCase(IApplicationDbContext db, IEnumerable<IPaymentGateway> gateways)
    {
        _db = db;
        _gateways = gateways;
    }

    public async Task<AppResult<PaymentResponse>> ExecuteAsync(int checkoutId, int userId, CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        var checkout = await CheckoutSessionQueries.LoadAsync(_db, checkoutId, userId, cancellationToken);
        if (checkout is null) return AppResult<PaymentResponse>.Failure(AppErrorType.NotFound, "Checkout no encontrado.");
        if (checkout.Status != CheckoutStatus.AwaitingPayment)
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El checkout ya no admite pagos.");
        return await CheckoutPaymentCreator.CreateAsync(_db, _gateways, checkout, request, PaymentCreationPolicy.Default, cancellationToken);
    }
}

internal static class CheckoutPaymentCreator
{
    public static async Task<AppResult<PaymentResponse>> CreateAsync(
        IApplicationDbContext db,
        IEnumerable<IPaymentGateway> gateways,
        CheckoutSession checkout,
        CreatePaymentRequest request,
        PaymentCreationPolicy policy,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"checkout-{checkout.Id}-{Guid.NewGuid():N}"
            : request.IdempotencyKey.Trim();
        if (idempotencyKey.Length > ValidationLimits.IdempotencyKey)
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "La clave de idempotencia no puede superar 100 caracteres.");

        var existing = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (existing.CheckoutSessionId != checkout.Id)
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada en otro checkout.");
            if (existing.Status != PaymentStatus.Creating || (existing.UpdatedAt ?? existing.CreatedAt) > DateTime.UtcNow.Subtract(policy.CreatingTimeout))
                return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(existing));
        }

        var active = existing ?? await db.Payments.AsNoTracking()
            .Where(x => x.CheckoutSessionId == checkout.Id && (x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending))
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (active is not null && active.Status != PaymentStatus.Creating)
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(active));

        var provider = string.IsNullOrWhiteSpace(request.Provider) ? "BankTransfer" : request.Provider.Trim();
        var gateway = gateways.FirstOrDefault(x => x.CanHandle(provider));
        if (gateway is null)
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, $"Proveedor de pago no soportado: {provider}.");

        Payment reservation;
        if (active is null)
        {
            reservation = new Payment
            {
                CheckoutSessionId = checkout.Id,
                Provider = provider,
                ExternalReference = $"checkout-{checkout.Id}",
                IdempotencyKey = idempotencyKey,
                Amount = checkout.Total,
                Currency = "ARS",
                Status = PaymentStatus.Creating,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Payments.Add(reservation);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                db.Payments.Remove(reservation);
                var winner = await db.Payments.AsNoTracking()
                    .Where(x => x.IdempotencyKey == idempotencyKey ||
                                (x.CheckoutSessionId == checkout.Id &&
                                 (x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending)))
                    .OrderByDescending(x => x.IdempotencyKey == idempotencyKey)
                    .ThenByDescending(x => x.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (winner is null) throw;
                if (winner.IdempotencyKey == idempotencyKey && winner.CheckoutSessionId != checkout.Id)
                    return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "La clave de idempotencia ya fue usada en otro checkout.");
                return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(winner));
            }
        }
        else
        {
            reservation = await db.Payments.SingleAsync(x => x.Id == active.Id, cancellationToken);
            reservation.UpdatedAt = DateTime.UtcNow;
        }

        reservation.ExternalReference = PaymentExternalReferences.BuildCheckout(checkout.Id, reservation.Id);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var preference = await gateway.CreatePreferenceAsync(checkout, reservation.IdempotencyKey, reservation.ExternalReference, cancellationToken);
            reservation.Provider = preference.Provider;
            reservation.ProviderPreferenceId = preference.ProviderPreferenceId;
            reservation.CheckoutUrl = preference.CheckoutUrl;
            reservation.Status = PaymentStatus.Pending;
            reservation.FailureReason = null;
            reservation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(reservation));
        }
        catch (PaymentGatewayException exception)
        {
            reservation.Status = PaymentStatus.CreationFailed;
            reservation.FailureReason = exception.Message;
            reservation.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Failure(AppErrorType.Unavailable, exception.Message, "payment_creation_failed");
        }
    }
}
public class GetPaymentByIdUseCase : IGetPaymentByIdUseCase
{
    private readonly IApplicationDbContext _db;

    public GetPaymentByIdUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<PaymentResponse>> ExecuteAsync(int id, int userId, bool canManageAll, CancellationToken cancellationToken = default)
    {
        var payment = await _db.Payments
            .AsNoTracking()
            .Include(x => x.Order!)
            .Include(x => x.CheckoutSession)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (payment is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.NotFound, "Pago no encontrado.");
        }

        var ownerId = payment.Order?.UserId ?? payment.CheckoutSession?.UserId;
        if (ownerId != userId && !canManageAll)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Forbidden, "No tenes permisos para ver este pago.");
        }

        return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
    }
}

public class GetOrderPaymentsUseCase : IGetOrderPaymentsUseCase
{
    private readonly IApplicationDbContext _db;

    public GetOrderPaymentsUseCase(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AppResult<List<PaymentResponse>>> ExecuteAsync(int orderId, int userId, bool canManageAll, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null)
        {
            return AppResult<List<PaymentResponse>>.Failure(AppErrorType.NotFound, "Pedido no encontrado.");
        }

        if (order.UserId != userId && !canManageAll)
        {
            return AppResult<List<PaymentResponse>>.Failure(AppErrorType.Forbidden, "No tenes permisos para ver los pagos de este pedido.");
        }

        var payments = await _db.Payments
            .AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .OrderByDescending(x => x.Id)
            .Select(x => PaymentMapper.ToResponse(x))
            .ToListAsync(cancellationToken);

        return AppResult<List<PaymentResponse>>.Success(payments);
    }
}

public class UpdatePaymentStatusUseCase : IUpdatePaymentStatusUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext? _auditContext;
    private readonly ITransactionManager? _transactionManager;

    public UpdatePaymentStatusUseCase(IApplicationDbContext db, IAuditContext? auditContext = null, ITransactionManager? transactionManager = null)
    {
        _db = db;
        _auditContext = auditContext;
        _transactionManager = transactionManager;
    }

    public async Task<AppResult<PaymentResponse>> ExecuteAsync(int id, UpdatePaymentStatusRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ProviderPaymentId?.Trim().Length > ValidationLimits.PaymentProviderId)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "El identificador del proveedor no puede superar 100 caracteres.");
        }

        if (request.FailureReason?.Trim().Length > ValidationLimits.PaymentFailureReason)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "El motivo no puede superar 500 caracteres.");
        }

        if (!Enum.TryParse<PaymentStatus>(request.Status, true, out var newStatus) || !Enum.IsDefined(newStatus))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "Estado de pago invalido.");
        }

        await using var transaction = _transactionManager is null ? null : await _transactionManager.BeginCheckoutTransactionAsync(cancellationToken);
        var payment = await PaymentQueries.LoadTrackedPaymentAsync(_db, id, cancellationToken);
        if (payment is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.NotFound, "Pago no encontrado.");
        }

        if ((newStatus is PaymentStatus.Approved or PaymentStatus.Rejected or PaymentStatus.Canceled or PaymentStatus.Expired) &&
            string.Equals(payment.Provider, "MercadoPago", StringComparison.OrdinalIgnoreCase))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "Mercado Pago solo puede resolverse mediante una notificacion verificada del proveedor.");
        }

        if (newStatus == PaymentStatus.Approved && string.Equals(payment.Provider, "BankTransfer", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(request.FailureReason))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "La referencia o motivo de la acreditacion es obligatorio.");
        }

        var result = await PaymentStatusApplier.ApplyAsync(
            _db,
            payment,
            newStatus,
            request.ProviderPaymentId,
            request.FailureReason,
            isProviderNotification: false,
            _auditContext,
            cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

public class HandlePaymentWebhookUseCase : IHandlePaymentWebhookUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IEnumerable<IPaymentGateway> _gateways;
    private readonly IAuditContext? _auditContext;
    private readonly ITransactionManager? _transactionManager;

    public HandlePaymentWebhookUseCase(IApplicationDbContext db, IEnumerable<IPaymentGateway> gateways, IAuditContext? auditContext = null, ITransactionManager? transactionManager = null)
    {
        _db = db;
        _gateways = gateways;
        _auditContext = auditContext;
        _transactionManager = transactionManager;
    }

    public async Task<AppResult<PaymentResponse>> ExecuteAsync(string provider, string providerPaymentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerPaymentId))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "El id del pago del proveedor es obligatorio.");
        }

        var gateway = _gateways.FirstOrDefault(x => x.CanHandle(provider));
        if (gateway is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, $"Proveedor de pago no soportado: {provider}.");
        }

        ProviderPaymentResult providerPayment;
        try
        {
            providerPayment = await gateway.GetPaymentAsync(providerPaymentId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, ex.Message);
        }

        var orderId = PaymentExternalReferences.TryGetOrderId(providerPayment.ExternalReference);
        var checkoutId = PaymentExternalReferences.TryGetCheckoutId(providerPayment.ExternalReference);
        if (orderId is null && checkoutId is null)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "La referencia externa del pago no corresponde a un checkout válido.");
        }

        await using var transaction = _transactionManager is null ? null : await _transactionManager.BeginCheckoutTransactionAsync(cancellationToken);
        var paymentQuery = _db.Payments
            .Include(x => x.Order!)
            .ThenInclude(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Order!)
            .ThenInclude(x => x.CouponRedemption)
            .Include(x => x.CheckoutSession!)
            .ThenInclude(x => x.Items)
            .Where(x => x.Provider == provider);

        var payment = await paymentQuery.SingleOrDefaultAsync(
            x => x.ProviderPaymentId == providerPayment.ProviderPaymentId,
            cancellationToken);

        if (payment is null)
        {
            var referenceMatches = await paymentQuery
                .Where(x => x.ExternalReference == providerPayment.ExternalReference)
                .ToListAsync(cancellationToken);
            if (referenceMatches.Count != 1)
            {
                var reason = referenceMatches.Count == 0
                    ? "La notificacion de Mercado Pago no coincide con ningun intento local."
                    : "La referencia legacy de Mercado Pago coincide con multiples intentos; requiere revision manual.";
                var incident = new
                {
                    providerPayment.ProviderPaymentId,
                    providerPayment.ExternalReference,
                    matches = referenceMatches.Count
                };
                var entityType = checkoutId.HasValue ? "CheckoutSession" : "Order";
                var entityId = checkoutId ?? orderId!.Value;
                if (!await HasUnmatchedIncidentAsync(entityType, entityId, incident.ProviderPaymentId, incident.ExternalReference, incident.matches, cancellationToken))
                {
                    AuditTrail.Add(_db, _auditContext, "PaymentWebhookUnmatched", entityType, entityId,
                        null,
                        incident,
                        reason);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, reason);
            }

            payment = referenceMatches[0];
        }

        if (payment.ProviderPaymentId is not null &&
            !string.Equals(payment.ProviderPaymentId, providerPayment.ProviderPaymentId, StringComparison.Ordinal))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "La notificacion no corresponde al intento de pago original.");
        }

        if (payment.ProviderPaymentId is null)
        {
            payment.ProviderPaymentId = providerPayment.ProviderPaymentId;
        }

        if (payment.Amount != providerPayment.Amount || !string.Equals(payment.Currency, providerPayment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El monto o la moneda del pago no coinciden con el checkout.");
        }

        if (PaymentStatusMapper.IsPartialRefund(providerPayment.Status))
        {
            if (payment.Status != PaymentStatus.Approved || payment.Order is null ||
                payment.Order.Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered))
            {
                return AppResult<PaymentResponse>.Failure(
                    AppErrorType.Conflict,
                    "El pago y el pedido no se encuentran en un estado compatible con un reembolso parcial.");
            }

            payment.FailureReason = "Reembolso parcial informado por el proveedor; requiere gestion manual.";
            payment.UpdatedAt = DateTime.UtcNow;
            AuditTrail.Add(_db, _auditContext, "PaymentPartialRefundFlagged", "Payment", payment.Id,
                new { status = payment.Status.ToString(), failureReason = (string?)null },
                new { status = payment.Status.ToString(), payment.FailureReason }, payment.FailureReason);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        var status = PaymentStatusMapper.FromProviderStatus(providerPayment.Status);
        if (status is null)
        {
            payment.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        var applyResult = await PaymentStatusApplier.ApplyAsync(
            _db,
            payment,
            status.Value,
            providerPayment.ProviderPaymentId,
            providerPayment.FailureReason,
            isProviderNotification: true,
            _auditContext,
            cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        if (!applyResult.IsSuccess && applyResult.Error?.Code == "paid_checkout_stock_unavailable")
        {
            try
            {
                if (await gateway.RefundPaymentAsync(providerPayment.ProviderPaymentId, cancellationToken))
                {
                    payment.Status = PaymentStatus.Refunded;
                    payment.FailureReason = "Pago devuelto automáticamente porque la compra ya no podía confirmarse.";
                    payment.UpdatedAt = DateTime.UtcNow;
                    if (payment.CheckoutSession is not null) payment.CheckoutSession.Status = CheckoutStatus.Refunded;
                    await _db.SaveChangesAsync(cancellationToken);
                    return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
                }
            }
            catch (PaymentGatewayException exception)
            {
                payment.FailureReason = $"No había stock y la devolución automática falló: {exception.Message}";
                await _db.SaveChangesAsync(cancellationToken);
            }
            return applyResult;
        }

        if (!applyResult.IsSuccess || status != PaymentStatus.Approved ||
            string.IsNullOrWhiteSpace(payment.ProviderPreferenceId))
        {
            return applyResult;
        }

        try
        {
            await gateway.ExpirePreferenceAsync(payment.ProviderPreferenceId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            var alreadyRecorded = await _db.AuditEntries.AsNoTracking().AnyAsync(
                x => x.Action == "PaymentPreferenceInvalidationFailed" &&
                     x.EntityType == "Payment" &&
                     x.EntityId == payment.Id.ToString(),
                cancellationToken);
            if (!alreadyRecorded)
            {
                AuditTrail.Add(_db, _auditContext, "PaymentPreferenceInvalidationFailed", "Payment", payment.Id,
                    null,
                    new { payment.ProviderPreferenceId },
                    ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return AppResult<PaymentResponse>.Failure(
                AppErrorType.Unavailable,
                "El pago fue confirmado, pero no se pudo cerrar el checkout. La notificacion puede reintentarse.",
                "payment_preference_invalidation_failed");
        }

        return applyResult;
    }

    private async Task<bool> HasUnmatchedIncidentAsync(
        string entityType,
        int entityId,
        string providerPaymentId,
        string externalReference,
        int matches,
        CancellationToken cancellationToken)
    {
        var recordedValues = await _db.AuditEntries
            .AsNoTracking()
            .Where(x => x.Action == "PaymentWebhookUnmatched" &&
                        x.EntityType == entityType &&
                        x.EntityId == entityId.ToString() &&
                        x.NewValue != null)
            .Select(x => x.NewValue!)
            .ToListAsync(cancellationToken);

        return recordedValues.Any(value => IsSameUnmatchedIncident(
            value,
            providerPaymentId,
            externalReference,
            matches));
    }

    private static bool IsSameUnmatchedIncident(
        string value,
        string providerPaymentId,
        string externalReference,
        int matches)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            var root = document.RootElement;
            return root.TryGetProperty("providerPaymentId", out var recordedPaymentId) &&
                   string.Equals(recordedPaymentId.GetString(), providerPaymentId, StringComparison.Ordinal) &&
                   root.TryGetProperty("externalReference", out var recordedReference) &&
                   string.Equals(recordedReference.GetString(), externalReference, StringComparison.Ordinal) &&
                   root.TryGetProperty("matches", out var recordedMatches) &&
                   recordedMatches.TryGetInt32(out var count) && count == matches;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal static class PaymentQueries
{
    public static Task<Payment?> LoadTrackedPaymentAsync(IApplicationDbContext db, int id, CancellationToken cancellationToken)
    {
        return db.Payments
            .Include(x => x.Order!)
            .ThenInclude(x => x.Items)
            .ThenInclude(x => x.Product)
            .Include(x => x.Order!)
            .ThenInclude(x => x.Items)
            .ThenInclude(x => x.ProductVariant)
            .Include(x => x.Order!)
            .ThenInclude(x => x.CouponRedemption)
            .Include(x => x.CheckoutSession!)
            .ThenInclude(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}

internal static class PaymentStatusApplier
{
    public static async Task<AppResult<PaymentResponse>> ApplyAsync(
        IApplicationDbContext db,
        Payment payment,
        PaymentStatus newStatus,
        string? providerPaymentId,
        string? failureReason,
        bool isProviderNotification,
        IAuditContext? auditContext,
        CancellationToken cancellationToken)
    {
        if (newStatus == PaymentStatus.Approved && payment.CheckoutSession?.Status == CheckoutStatus.StockUnavailable)
        {
            return AppResult<PaymentResponse>.Failure(
                AppErrorType.Conflict,
                "El pago fue acreditado, pero la compra ya no podía confirmarse.",
                "paid_checkout_stock_unavailable");
        }

        if (payment.Status == newStatus)
        {
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        if (payment.CheckoutSession is not null && payment.Order is null)
        {
            if (payment.Status != PaymentStatus.Pending)
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "Transición de estado de pago inválida.");

            payment.ProviderPaymentId = NormalizeProviderPaymentId(payment.ProviderPaymentId, providerPaymentId);
            payment.UpdatedAt = DateTime.UtcNow;
            if (newStatus == PaymentStatus.Pending)
            {
                await db.SaveChangesAsync(cancellationToken);
                return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
            }

            if (newStatus == PaymentStatus.Approved)
            {
                payment.PaidAt = DateTime.UtcNow;
                var completion = await CheckoutCompletion.CompleteAsync(db, payment.CheckoutSession, payment, auditContext, cancellationToken);
                return completion.IsSuccess
                    ? AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment))
                    : AppResult<PaymentResponse>.Failure(completion.Error!.Type, completion.Error.Message, completion.Error.Code);
            }

            if (newStatus is PaymentStatus.Rejected or PaymentStatus.Canceled or PaymentStatus.Expired)
            {
                payment.Status = newStatus;
                payment.FailureReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
                payment.CheckoutSession.Status = CheckoutStatus.PaymentFailed;
                payment.CheckoutSession.CompletedAtUtc = DateTime.UtcNow;
                AuditTrail.Add(db, auditContext,
                    isProviderNotification ? "CheckoutPaymentResolvedByProvider" : "CheckoutPaymentResolvedManually",
                    "Payment", payment.Id,
                    new { paymentStatus = PaymentStatus.Pending.ToString(), checkoutStatus = CheckoutStatus.AwaitingPayment.ToString() },
                    new { paymentStatus = newStatus.ToString(), checkoutStatus = payment.CheckoutSession.Status.ToString() },
                    payment.FailureReason);
                await db.SaveChangesAsync(cancellationToken);
                return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
            }

            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "Estado de pago no permitido para un checkout sin orden.");
        }

        if (isProviderNotification && newStatus == PaymentStatus.Approved &&
            payment.Status == PaymentStatus.Canceled && payment.Order?.Status == OrderStatus.Canceled)
        {
            var canceledPaymentStatus = payment.Status;
            const string incident = "Mercado Pago informo una aprobacion despues de la cancelacion administrativa; requiere revision y devolucion.";
            payment.Status = PaymentStatus.Approved;
            payment.ProviderPaymentId = NormalizeProviderPaymentId(payment.ProviderPaymentId, providerPaymentId);
            payment.FailureReason = incident;
            payment.PaidAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            AuditTrail.Add(db, auditContext, "PaymentApprovedAfterOrderCancellation", "Payment", payment.Id,
                new { paymentStatus = canceledPaymentStatus.ToString(), orderStatus = payment.Order.Status.ToString() },
                new { paymentStatus = payment.Status.ToString(), orderStatus = payment.Order.Status.ToString(), incident = true },
                incident);
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        if (newStatus == PaymentStatus.Refunded)
        {
            if (!isProviderNotification)
            {
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "Un reembolso solo puede aplicarse despues de ser confirmado por el proveedor.");
            }

            if (payment.Status != PaymentStatus.Approved || payment.Order is null ||
                payment.Order.Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.Shipped or OrderStatus.Delivered))
            {
                return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pago y el pedido no se encuentran en un estado reembolsable.");
            }

            var oldPaymentStatus = payment.Status;
            var oldOrderStatus = payment.Order.Status;
            var requiresManualReturn = payment.Order.Status is OrderStatus.Shipped or OrderStatus.Delivered;
            OrderCompensation.ApplyConfirmedRefund(db, payment.Order,
                "Reposicion por reembolso total confirmado.", auditContext?.ActorUserId);
            payment.Status = PaymentStatus.Refunded;
            payment.ProviderPaymentId = NormalizeProviderPaymentId(payment.ProviderPaymentId, providerPaymentId);
            payment.FailureReason = string.IsNullOrWhiteSpace(failureReason)
                ? requiresManualReturn
                    ? "Reembolso total confirmado; devolucion y stock requieren gestion manual."
                    : "Reembolso total confirmado por el proveedor."
                : failureReason.Trim();
            payment.UpdatedAt = DateTime.UtcNow;
            AuditTrail.Add(db, auditContext, "PaymentRefundedByProvider", "Payment", payment.Id,
                new { paymentStatus = oldPaymentStatus.ToString(), orderStatus = oldOrderStatus.ToString() },
                new { paymentStatus = payment.Status.ToString(), orderStatus = payment.Order.Status.ToString() },
                payment.FailureReason);
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        if (payment.Status != PaymentStatus.Pending)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "Transicion de estado de pago invalida.");
        }

        if (newStatus == PaymentStatus.Pending)
        {
            payment.ProviderPaymentId = NormalizeProviderPaymentId(payment.ProviderPaymentId, providerPaymentId);
            payment.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
        }

        if (payment.Order is null)
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pago no está asociado a una orden o checkout válido.");

        if (payment.Order.Status != OrderStatus.Pending)
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Conflict, "El pedido ya no esta pendiente de pago.");
        }

        var previousPaymentStatus = payment.Status;
        var previousOrderStatus = payment.Order.Status;
        payment.Status = newStatus;
        payment.ProviderPaymentId = NormalizeProviderPaymentId(payment.ProviderPaymentId, providerPaymentId);
        var resolutionReason = string.IsNullOrWhiteSpace(failureReason) ? null : failureReason.Trim();
        payment.FailureReason = resolutionReason;
        payment.UpdatedAt = DateTime.UtcNow;

        if (newStatus == PaymentStatus.Approved)
        {
            payment.PaidAt = DateTime.UtcNow;
            if (!payment.Order.StockReserved)
            {
                var stockError = ReserveStockAfterGuestTransfer(db, payment.Order, auditContext?.ActorUserId);
                if (stockError is not null)
                {
                    payment.Status = PaymentStatus.Approved;
                    payment.FailureReason = $"{stockError} Contactar al cliente para ofrecer un cambio o gestionar la devolución.";
                    payment.Order.Status = OrderStatus.Canceled;
                    payment.Order.CancellationReason = payment.FailureReason;
                    payment.Order.UpdatedAt = DateTime.UtcNow;
                    AuditTrail.Add(db, auditContext, "GuestTransferPaidWithoutStock", "Payment", payment.Id,
                        new { paymentStatus = previousPaymentStatus.ToString(), orderStatus = previousOrderStatus.ToString() },
                        new { paymentStatus = payment.Status.ToString(), orderStatus = payment.Order.Status.ToString(), requiresReview = true },
                        payment.FailureReason);
                    await db.SaveChangesAsync(cancellationToken);
                    return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
                }
            }
            payment.FailureReason = null;
            payment.Order.Status = OrderStatus.Paid;
            payment.Order.UpdatedAt = DateTime.UtcNow;
            CouponRedemptionLifecycle.Consume(payment.Order);
        }
        else if (newStatus is PaymentStatus.Rejected or PaymentStatus.Canceled or PaymentStatus.Expired)
        {
            OrderCompensation.CancelPendingAndRestoreStock(
                db, payment.Order,
                payment.FailureReason ?? $"Pago resuelto como {newStatus}.", auditContext?.ActorUserId);
        }
        else
        {
            return AppResult<PaymentResponse>.Failure(AppErrorType.Validation, "Solo se puede resolver un pago pendiente como Approved, Rejected, Canceled o Expired.");
        }

        AuditTrail.Add(db, auditContext,
            isProviderNotification ? "PaymentResolvedByProvider" : "PaymentResolvedManually",
            "Payment", payment.Id,
            new { paymentStatus = previousPaymentStatus.ToString(), orderStatus = previousOrderStatus.ToString() },
            new { paymentStatus = payment.Status.ToString(), orderStatus = payment.Order.Status.ToString() },
            resolutionReason);

        await db.SaveChangesAsync(cancellationToken);
        return AppResult<PaymentResponse>.Success(PaymentMapper.ToResponse(payment));
    }

    private static string? ReserveStockAfterGuestTransfer(IApplicationDbContext db, Order order, int? actorUserId)
    {
        foreach (var item in order.Items)
        {
            if (!item.Product.IsActive) return $"{item.ProductName} ya no está disponible.";
            if (item.ProductVariantId.HasValue && (item.ProductVariant is null || !item.ProductVariant.IsActive))
                return $"La variante de {item.ProductName} ya no está disponible.";
            if ((item.ProductVariant?.Stock ?? item.Product.Stock) < item.Quantity)
                return $"No queda stock suficiente de {item.ProductName}.";
        }

        foreach (var item in order.Items)
        {
            var previous = item.ProductVariant?.Stock ?? item.Product.Stock;
            if (item.ProductVariant is null) item.Product.Stock -= item.Quantity;
            else item.ProductVariant.Stock -= item.Quantity;
            item.Product.UpdatedAt = DateTime.UtcNow;
            StockMovementRecorder.Add(db, item.Product, StockMovementType.Sale, -item.Quantity, previous,
                "Venta confirmada después de acreditar una transferencia de invitado.", actorUserId, order, item.ProductVariant);
        }
        order.StockReserved = true;
        return null;
    }

    private static string? NormalizeProviderPaymentId(string? currentValue, string? newValue) =>
        string.IsNullOrWhiteSpace(newValue) ? currentValue : newValue.Trim();
}

internal static class PaymentStatusMapper
{
    public static bool IsPartialRefund(string status) =>
        string.Equals(status, "partially_refunded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "partially-refunded", StringComparison.OrdinalIgnoreCase);

    public static PaymentStatus? FromProviderStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "approved" => PaymentStatus.Approved,
            "rejected" => PaymentStatus.Rejected,
            "cancelled" => PaymentStatus.Canceled,
            "canceled" => PaymentStatus.Canceled,
            "expired" => PaymentStatus.Expired,
            "refunded" => PaymentStatus.Refunded,
            "pending" => PaymentStatus.Pending,
            "in_process" => PaymentStatus.Pending,
            "in_mediation" => PaymentStatus.Pending,
            _ => null
        };
    }
}

internal static class PaymentExternalReferences
{
    public static string Build(int orderId, int paymentId) => $"order-{orderId}-payment-{paymentId}";
    public static string BuildCheckout(int checkoutId, int paymentId) => $"checkout-{checkoutId}-payment-{paymentId}";

    public static int? TryGetCheckoutId(string externalReference)
    {
        const string prefix = "checkout-";
        if (!externalReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var value = externalReference[prefix.Length..];
        var separator = value.IndexOf("-payment-", StringComparison.OrdinalIgnoreCase);
        if (separator >= 0) value = value[..separator];
        return int.TryParse(value, out var checkoutId) ? checkoutId : null;
    }

    public static int? TryGetOrderId(string externalReference)
    {
        const string prefix = "order-";
        if (!externalReference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var value = externalReference[prefix.Length..];
        var separator = value.IndexOf("-payment-", StringComparison.OrdinalIgnoreCase);
        if (separator >= 0) value = value[..separator];
        return int.TryParse(value, out var orderId)
            ? orderId
            : null;
    }
}

internal static class PaymentMapper
{
    public static PaymentResponse ToResponse(Payment payment)
    {
        return new PaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.CheckoutSessionId,
            payment.Provider,
            payment.ExternalReference,
            payment.ProviderPreferenceId,
            payment.ProviderPaymentId,
            payment.IdempotencyKey,
            payment.Amount,
            payment.Currency,
            payment.Status.ToString(),
            payment.CheckoutUrl,
            payment.FailureReason,
            payment.CreatedAt,
            payment.UpdatedAt,
            payment.PaidAt
        );
    }
}
