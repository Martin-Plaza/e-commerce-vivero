using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Stock;

public sealed record StockMovementQuery(
    [Range(1, int.MaxValue)] int Page = 1,
    [Range(1, 100)] int PageSize = 25,
    int? ProductId = null,
    [StringLength(40)] string? Type = null,
    int? OrderId = null,
    int? ActorUserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

public sealed record ManualStockAdjustmentRequest(
    int Quantity,
    [Required, StringLength(500)] string Reason,
    int? ProductVariantId = null);

public sealed record StockMovementResponse(long Id, int ProductId, string ProductName, string Type,
    int Quantity, int PreviousStock, int ResultingStock, string Reason, int? ActorUserId,
    string? ActorName, int? OrderId, DateTime CreatedAtUtc, int? ProductVariantId = null, string? VariantSku = null);

public sealed record PagedStockMovementsResponse(List<StockMovementResponse> Items, int Page, int PageSize,
    long TotalItems, int TotalPages);

public sealed record StockAdjustmentResponse(int ProductId, int PreviousStock, int ResultingStock,
    StockMovementResponse Movement);
