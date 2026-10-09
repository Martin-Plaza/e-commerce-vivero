using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Users;

public record AdminUserResponse(int Id, string Email, string Name, string Role, bool IsActive, DateTime CreatedAt);
public sealed record UserFilterRequest([Range(1, int.MaxValue)] int Page = 1, [Range(1, 100)] int PageSize = 20, [StringLength(150)] string? Search = null, [StringLength(30)] string? Role = null, bool? IsActive = null);
public sealed record PagedUsersResponse(List<AdminUserResponse> Items, int Page, int PageSize, long TotalItems, int TotalPages);
public sealed record UserOrderSummaryResponse(int Id, DateTime CreatedAt, decimal Total, string Status, string DeliveryMethod);
public sealed record AdminUserDetailResponse(int Id, string Email, string Name, string Role, bool IsActive, DateTime CreatedAt, long OrderCount, decimal TotalPurchased, DateTime? LastOrderAt, long OrdersTotal, int OrdersPageSize, List<UserOrderSummaryResponse> RecentOrders);
public record CreateUserRequest([Required, StringLength(150)] string Name, [Required, EmailAddress, StringLength(254)] string Email, [Required, MinLength(6), StringLength(200)] string Password, [Required, StringLength(30)] string Role);
public record UpdateUserRoleRequest([Required, StringLength(30)] string Role);
public record UpdateUserStatusRequest(bool IsActive);
