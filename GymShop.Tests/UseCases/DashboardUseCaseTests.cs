using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Dashboard;
using GymShop.Application.UseCases.Dashboard;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public sealed class DashboardUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Dashboard_counts_audited_free_order_without_inventing_a_payment()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Regalo", Price = 100, Stock = 8, IsActive = true };
        var user = NewUser();
        db.AddRange(product, user);
        await db.SaveChangesAsync();
        var order = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Paid, (product, 1, 100m));
        order.Total = 0;
        await db.SaveChangesAsync();
        db.AuditEntries.Add(new AuditEntry
        {
            Action = "FreeOrderConfirmed", EntityType = "Order", EntityId = order.Id.ToString(),
            CorrelationId = "free-test", CreatedAtUtc = Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 18), To: new DateOnly(2026, 9, 21)));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.PaidOrders);
        Assert.Equal(0, result.Value.TotalSales);
        Assert.Equal(0, result.Value.AverageTicket);
        Assert.Single(result.Value.TopProducts);
        Assert.Empty(db.Payments);
    }

    [Fact]
    public async Task Dashboard_deduplicates_payment_attempts_and_calculates_ticket_daily_sales_and_top_products()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Mancuerna", Price = 100, Stock = 8, IsActive = true };
        var second = new Product { Name = "Banda", Price = 50, Stock = 3, IsActive = true };
        var user = NewUser();
        db.AddRange(product, second, user);
        await db.SaveChangesAsync();
        var firstOrder = AddOrder(db, user.Id, Now.AddDays(-2), OrderStatus.Paid, (product, 2, 200m));
        var secondOrder = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Delivered, (second, 1, 50m));
        AddPayment(db, firstOrder, PaymentStatus.Rejected, Now.AddDays(-2));
        AddPayment(db, firstOrder, PaymentStatus.Approved, Now.AddDays(-2));
        AddPayment(db, firstOrder, PaymentStatus.Approved, Now.AddDays(-2).AddMinutes(5));
        AddPayment(db, secondOrder, PaymentStatus.Approved, Now.AddDays(-1));
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 18), To: new DateOnly(2026, 9, 21)));

        Assert.True(result.IsSuccess);
        Assert.Equal(250m, result.Value!.TotalSales);
        Assert.Equal(2, result.Value.PaidOrders);
        Assert.Equal(125m, result.Value.AverageTicket);
        Assert.Equal(2, result.Value.SalesByDay.Count);
        Assert.Equal(2, result.Value.TopProducts[0].Quantity);
        Assert.Equal(200m, result.Value.TopProducts[0].Amount);
    }

    [Fact]
    public async Task Dashboard_does_not_recount_when_first_approval_is_before_range_and_another_is_inside()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Disco", Price = 80, Stock = 8 };
        var user = NewUser(); db.AddRange(product, user); await db.SaveChangesAsync();
        var order = AddOrder(db, user.Id, Now.AddDays(-10), OrderStatus.Paid, (product, 2, 160m));
        AddPayment(db, order, PaymentStatus.Approved, new DateTime(2026, 9, 10, 15, 0, 0, DateTimeKind.Utc));
        AddPayment(db, order, PaymentStatus.Approved, new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 18), To: new DateOnly(2026, 9, 21)));

        Assert.Equal(0, result.Value!.PaidOrders);
        Assert.Equal(0m, result.Value.TotalSales);
        Assert.Empty(result.Value.TopProducts);
    }

    [Fact]
    public async Task Dashboard_counts_once_when_first_approval_is_inside_and_another_is_after_range()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Soga", Price = 90, Stock = 8 };
        var user = NewUser(); db.AddRange(product, user); await db.SaveChangesAsync();
        var order = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Paid, (product, 3, 270m));
        AddPayment(db, order, PaymentStatus.Approved, new DateTime(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc));
        AddPayment(db, order, PaymentStatus.Approved, new DateTime(2026, 9, 22, 15, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 18), To: new DateOnly(2026, 9, 21)));

        Assert.Equal(1, result.Value!.PaidOrders);
        Assert.Equal(270m, result.Value.TotalSales);
        var top = Assert.Single(result.Value.TopProducts);
        Assert.Equal(3, top.Quantity);
        Assert.Equal(270m, top.Amount);
    }

    [Fact]
    public async Task Dashboard_excludes_canceled_and_fully_refunded_orders_and_reports_statuses_and_stock()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var empty = new Product { Name = "Agotado", Price = 10, Stock = 0, IsActive = true };
        var low = new Product { Name = "Escaso", Price = 20, Stock = 5, IsActive = true };
        var inactive = new Product { Name = "Inactivo", Price = 20, Stock = 0, IsActive = false };
        var user = NewUser(); db.AddRange(empty, low, inactive, user); await db.SaveChangesAsync();
        var canceled = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Canceled, (empty, 1, 10m));
        AddPayment(db, canceled, PaymentStatus.Approved, Now.AddDays(-1));
        var refunded = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Refunded, (low, 1, 20m));
        AddPayment(db, refunded, PaymentStatus.Refunded, Now.AddDays(-1));
        var partial = AddOrder(db, user.Id, Now.AddDays(-1), OrderStatus.Paid, (low, 1, 20m));
        AddPayment(db, partial, PaymentStatus.Approved, Now.AddDays(-1));
        await db.SaveChangesAsync();
        var partialPayment = partial.Payments.Single();
        db.AuditEntries.Add(new AuditEntry { Action = "PaymentPartialRefundFlagged", EntityType = "Payment", EntityId = partialPayment.Id.ToString(), CorrelationId = "provider", CreatedAtUtc = Now });
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(Period: "7d"));

        Assert.Equal(0m, result.Value!.TotalSales);
        Assert.Equal(0, result.Value.PaidOrders);
        Assert.Contains(result.Value.OrdersByStatus, item => item.Status == "Canceled" && item.Count == 1);
        Assert.Contains(result.Value.OrdersByStatus, item => item.Status == "Refunded" && item.Count == 1);
        Assert.Contains(result.Value.OrdersByStatus, item => item.Status == "Paid" && item.Count == 1);
        Assert.Equal("Agotado", Assert.Single(result.Value.OutOfStockProducts).ProductName);
        Assert.Equal("Escaso", Assert.Single(result.Value.LowStockProducts).ProductName);
    }

    [Theory]
    [InlineData("invalid", null, null)]
    [InlineData(null, "2026-09-21", "2026-09-20")]
    [InlineData(null, "2025-01-01", "2026-09-21")]
    [InlineData(null, "2026-09-20", null)]
    public async Task Dashboard_rejects_invalid_ranges(string? period, string? from, string? to)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var request = new DashboardQueryRequest(period, from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to));
        var result = await UseCase(db).ExecuteAsync(request);
        Assert.False(result.IsSuccess);
        Assert.Equal(AppErrorType.Validation, result.Error?.Type);
    }

    [Fact]
    public async Task Dashboard_filters_sales_and_order_statuses_by_the_requested_dates()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Banco", Price = 300, Stock = 9 };
        var user = NewUser(); db.AddRange(product, user); await db.SaveChangesAsync();
        var outside = AddOrder(db, user.Id, Now.AddDays(-10), OrderStatus.Paid, (product, 1, 300m));
        AddPayment(db, outside, PaymentStatus.Approved, Now.AddDays(-10));
        var inside = AddOrder(db, user.Id, Now, OrderStatus.Paid, (product, 1, 300m));
        AddPayment(db, inside, PaymentStatus.Approved, Now);
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(Period: "7d"));
        Assert.Equal(300m, result.Value!.TotalSales);
        Assert.Equal(1, Assert.Single(result.Value.OrdersByStatus).Count);
    }

    [Fact]
    public async Task Dashboard_assigns_sale_near_utc_midnight_to_previous_commercial_day()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var product = new Product { Name = "Kettlebell", Price = 120, Stock = 3 };
        var user = NewUser(); db.AddRange(product, user); await db.SaveChangesAsync();
        var order = AddOrder(db, user.Id, Now, OrderStatus.Paid, (product, 1, 120m));
        AddPayment(db, order, PaymentStatus.Approved, new DateTime(2026, 9, 20, 2, 30, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();

        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 19), To: new DateOnly(2026, 9, 19)));

        var day = Assert.Single(result.Value!.SalesByDay);
        Assert.Equal(new DateOnly(2026, 9, 19), day.Date);
        Assert.Equal(new DateTime(2026, 9, 19, 3, 0, 0, DateTimeKind.Utc), result.Value.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 20, 3, 0, 0, DateTimeKind.Utc), result.Value.ToUtc);
    }

    [Fact]
    public async Task Dashboard_month_period_uses_commercial_month_boundaries()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var localLastDay = new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);
        var result = await UseCase(db, localLastDay).ExecuteAsync(new DashboardQueryRequest(Period: "month"));

        Assert.Equal(new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc), result.Value!.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc), result.Value.ToUtc);
    }

    [Fact]
    public async Task Dashboard_quick_period_uses_current_commercial_date()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var utcAlreadyNextDay = new DateTime(2026, 9, 22, 2, 0, 0, DateTimeKind.Utc);
        var result = await UseCase(db, utcAlreadyNextDay).ExecuteAsync(new DashboardQueryRequest(Period: "7d"));

        Assert.Equal(new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc), result.Value!.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc), result.Value.ToUtc);
    }

    [Fact]
    public async Task Dashboard_custom_range_is_converted_from_commercial_dates_to_utc()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await UseCase(db).ExecuteAsync(new DashboardQueryRequest(From: new DateOnly(2026, 9, 1), To: new DateOnly(2026, 9, 2)));

        Assert.Equal(new DateTime(2026, 9, 1, 3, 0, 0, DateTimeKind.Utc), result.Value!.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 3, 3, 0, 0, DateTimeKind.Utc), result.Value.ToUtc);
        Assert.Equal(StoreTimeZone.DefaultId, result.Value.TimeZoneId);
    }

    [Fact]
    public void Store_timezone_uses_default_when_missing_and_utc_fallback_when_invalid()
    {
        var missing = new StoreTimeZone(null);
        var invalid = new StoreTimeZone("Invalid/CommercialZone");

        Assert.False(missing.IsFallback);
        Assert.Equal(StoreTimeZone.DefaultId, missing.Id);
        Assert.True(invalid.IsFallback);
        Assert.Equal(TimeZoneInfo.Utc.Id, invalid.Id);
    }

    private static GetDashboardStatisticsUseCase UseCase(GymShop.Infrastructure.Data.GymShopDbContext db, DateTime? now = null, string? timeZone = null) =>
        new(db, new FixedTimeProvider(now ?? Now), new StoreTimeZone(timeZone));
    private static User NewUser() => new() { Email = $"{Guid.NewGuid()}@test.com", PasswordHash = "hash", Name = "Cliente", RoleId = 1, IsActive = true, EmailVerifiedAt = Now };

    private static Order AddOrder(GymShop.Infrastructure.Data.GymShopDbContext db, int userId, DateTime createdAt, OrderStatus status, params (Product Product, int Quantity, decimal Subtotal)[] items)
    {
        var order = new Order { UserId = userId, CreatedAt = createdAt, Status = status, ShippingAddress = "Calle 123", Total = items.Sum(x => x.Subtotal) };
        foreach (var item in items) order.Items.Add(new OrderItem { ProductId = item.Product.Id, ProductName = item.Product.Name, UnitPrice = item.Subtotal / item.Quantity, Quantity = item.Quantity, Subtotal = item.Subtotal });
        db.Orders.Add(order); return order;
    }

    private static void AddPayment(GymShop.Infrastructure.Data.GymShopDbContext db, Order order, PaymentStatus status, DateTime paidAt) =>
        db.Payments.Add(new Payment { Order = order, Provider = "Mock", ExternalReference = Guid.NewGuid().ToString(), Amount = order.Total, Status = status, PaidAt = paidAt });

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
