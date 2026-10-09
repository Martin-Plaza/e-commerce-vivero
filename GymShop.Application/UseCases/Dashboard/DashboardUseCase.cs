using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Dashboard;
using GymShop.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Dashboard;

public interface IGetDashboardStatisticsUseCase
{
    Task<AppResult<DashboardResponse>> ExecuteAsync(DashboardQueryRequest request, CancellationToken cancellationToken = default);
}

public sealed class GetDashboardStatisticsUseCase : IGetDashboardStatisticsUseCase
{
    public const int LowStockThreshold = 5;
    public const int MaximumRangeDays = 366;
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IStoreTimeZone _storeTimeZone;

    public GetDashboardStatisticsUseCase(IApplicationDbContext db, TimeProvider timeProvider, IStoreTimeZone storeTimeZone)
    {
        _db = db;
        _timeProvider = timeProvider;
        _storeTimeZone = storeTimeZone;
    }

    public async Task<AppResult<DashboardResponse>> ExecuteAsync(DashboardQueryRequest request, CancellationToken cancellationToken = default)
    {
        var range = ResolveRange(request);
        if (!range.IsSuccess)
        {
            return AppResult<DashboardResponse>.Failure(range.Error!.Type, range.Error.Message);
        }

        var (fromUtc, toUtc) = range.Value;
        var ordersInRange = _db.Orders.AsNoTracking()
            .Where(order => order.CreatedAt >= fromUtc && order.CreatedAt < toUtc);

        // A sale is counted once per order, on its first approved payment. Refunded and
        // canceled orders are excluded even if an earlier payment attempt was approved.
        var paidSales = _db.Orders.AsNoTracking()
            .Where(order => order.Status != OrderStatus.Canceled && order.Status != OrderStatus.Refunded)
            .Where(order => !order.Payments.Any(payment => payment.Status == PaymentStatus.Refunded))
            .Where(order => !order.Payments.Any(payment => _db.AuditEntries.Any(audit =>
                audit.Action == "PaymentPartialRefundFlagged" && audit.EntityType == "Payment" && audit.EntityId == payment.Id.ToString())))
            .Where(order => order.Payments.Any(payment => payment.Status == PaymentStatus.Approved && payment.PaidAt.HasValue))
            .Select(order => new
            {
                order.Id,
                order.Total,
                SoldAt = order.Payments
                    .Where(payment => payment.Status == PaymentStatus.Approved && payment.PaidAt.HasValue)
                    .Min(payment => payment.PaidAt)!.Value
            })
            .Where(sale => sale.SoldAt >= fromUtc && sale.SoldAt < toUtc);
        var freeSales = _db.Orders.AsNoTracking()
            .Where(order => order.Status == OrderStatus.Paid && order.Total == 0)
            .Where(order => _db.AuditEntries.Any(audit =>
                audit.Action == "FreeOrderConfirmed" && audit.EntityType == "Order" && audit.EntityId == order.Id.ToString()))
            .Select(order => new
            {
                order.Id,
                order.Total,
                SoldAt = _db.AuditEntries
                    .Where(audit => audit.Action == "FreeOrderConfirmed" && audit.EntityType == "Order" && audit.EntityId == order.Id.ToString())
                    .Min(audit => audit.CreatedAtUtc)
            })
            .Where(sale => sale.SoldAt >= fromUtc && sale.SoldAt < toUtc);
        var sales = paidSales.Concat(freeSales);

        var totals = await sales
            .GroupBy(_ => 1)
            .Select(group => new { Amount = group.Sum(x => x.Total), Orders = group.Count() })
            .SingleOrDefaultAsync(cancellationToken);

        var statusRows = await ordersInRange
            .GroupBy(order => order.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .OrderBy(item => item.Status)
            .ToListAsync(cancellationToken);
        var ordersByStatus = statusRows
            .Select(item => new DashboardStatusCountResponse(item.Status.ToString(), item.Count))
            .ToList();

        // Buenos Aires has a stable UTC-03 offset. The offset is resolved from the configured
        // TimeZoneInfo instead of embedding it in the query/use case.
        var commercialOffsetMinutes = (int)_storeTimeZone.TimeZone.GetUtcOffset(fromUtc).TotalMinutes;
        var dailyRows = await sales
            .GroupBy(sale => sale.SoldAt.AddMinutes(commercialOffsetMinutes).Date)
            .Select(group => new { Date = group.Key, Amount = group.Sum(x => x.Total), Orders = group.Count() })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        var salesByDay = dailyRows
            .Select(item => new DashboardDailySalesResponse(DateOnly.FromDateTime(item.Date), item.Amount, item.Orders))
            .ToList();

        var topProductRows = await _db.OrderItems.AsNoTracking()
            .Where(item => sales.Any(sale => sale.Id == item.OrderId))
            .GroupBy(item => new { item.ProductId, item.ProductName })
            .Select(group => new { group.Key.ProductId, group.Key.ProductName, Quantity = group.Sum(item => item.Quantity), Amount = group.Sum(item => item.Subtotal) })
            .OrderByDescending(item => item.Quantity)
            .ThenByDescending(item => item.Amount)
            .ThenBy(item => item.ProductName)
            .Take(10)
            .ToListAsync(cancellationToken);
        var topProducts = topProductRows
            .Select(item => new DashboardTopProductResponse(item.ProductId, item.ProductName, item.Quantity, item.Amount))
            .ToList();

        var stockProducts = _db.Products.AsNoTracking().Where(product => product.IsActive);
        var outOfStockRows = await stockProducts
            .Where(product => product.Stock == 0)
            .OrderBy(product => product.Name)
            .Select(product => new { product.Id, product.Name, product.Stock, product.IsActive })
            .ToListAsync(cancellationToken);
        var lowStockRows = await stockProducts
            .Where(product => product.Stock > 0 && product.Stock <= LowStockThreshold)
            .OrderBy(product => product.Stock)
            .ThenBy(product => product.Name)
            .Select(product => new { product.Id, product.Name, product.Stock, product.IsActive })
            .ToListAsync(cancellationToken);
        var outOfStock = outOfStockRows.Select(product => new DashboardStockProductResponse(product.Id, product.Name, product.Stock, product.IsActive)).ToList();
        var lowStock = lowStockRows.Select(product => new DashboardStockProductResponse(product.Id, product.Name, product.Stock, product.IsActive)).ToList();

        var paidOrders = totals?.Orders ?? 0;
        var totalSales = totals?.Amount ?? 0;
        return AppResult<DashboardResponse>.Success(new DashboardResponse(
            fromUtc,
            toUtc,
            _storeTimeZone.Id,
            totalSales,
            paidOrders,
            paidOrders == 0 ? 0 : decimal.Round(totalSales / paidOrders, 2),
            ordersByStatus,
            salesByDay,
            topProducts,
            outOfStock,
            lowStock,
            LowStockThreshold));
    }

    private AppResult<(DateTime FromUtc, DateTime ToUtc)> ResolveRange(DashboardQueryRequest request)
    {
        var nowUtc = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _storeTimeZone.TimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        DateOnly from;
        DateOnly to;

        if (request.From.HasValue || request.To.HasValue)
        {
            if (!request.From.HasValue || !request.To.HasValue || !string.IsNullOrWhiteSpace(request.Period))
                return AppResult<(DateTime, DateTime)>.Failure(AppErrorType.Validation, "Indica fecha desde y hasta, sin combinar con un periodo rapido.");
            from = request.From.Value;
            to = request.To.Value;
        }
        else
        {
            switch ((request.Period ?? "30d").Trim().ToLowerInvariant())
            {
                case "7d": from = today.AddDays(-6); to = today; break;
                case "30d": from = today.AddDays(-29); to = today; break;
                case "month": from = new DateOnly(today.Year, today.Month, 1); to = today; break;
                default: return AppResult<(DateTime, DateTime)>.Failure(AppErrorType.Validation, "Periodo invalido. Usa 7d, 30d o month.");
            }
        }

        if (from > to)
            return AppResult<(DateTime, DateTime)>.Failure(AppErrorType.Validation, "La fecha desde no puede ser posterior a la fecha hasta.");
        if (to.DayNumber - from.DayNumber + 1 > MaximumRangeDays)
            return AppResult<(DateTime, DateTime)>.Failure(AppErrorType.Validation, $"El rango no puede superar {MaximumRangeDays} dias.");
        if (to > today)
            return AppResult<(DateTime, DateTime)>.Failure(AppErrorType.Validation, "La fecha hasta no puede estar en el futuro.");

        return AppResult<(DateTime, DateTime)>.Success((
            LocalMidnightToUtc(from),
            LocalMidnightToUtc(to.AddDays(1))));
    }

    private DateTime LocalMidnightToUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, _storeTimeZone.TimeZone);
    }
}
