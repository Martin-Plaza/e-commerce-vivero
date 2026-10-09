namespace GymShop.Application.DTOs.Dashboard;

public sealed record DashboardQueryRequest(
    string? Period = null,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record DashboardResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string TimeZoneId,
    decimal TotalSales,
    int PaidOrders,
    decimal AverageTicket,
    IReadOnlyList<DashboardStatusCountResponse> OrdersByStatus,
    IReadOnlyList<DashboardDailySalesResponse> SalesByDay,
    IReadOnlyList<DashboardTopProductResponse> TopProducts,
    IReadOnlyList<DashboardStockProductResponse> OutOfStockProducts,
    IReadOnlyList<DashboardStockProductResponse> LowStockProducts,
    int LowStockThreshold);

public sealed record DashboardStatusCountResponse(string Status, int Count);
public sealed record DashboardDailySalesResponse(DateOnly Date, decimal Amount, int Orders);
public sealed record DashboardTopProductResponse(int ProductId, string ProductName, int Quantity, decimal Amount);
public sealed record DashboardStockProductResponse(int ProductId, string ProductName, int Stock, bool IsActive);
