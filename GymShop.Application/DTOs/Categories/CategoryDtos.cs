using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Categories;

public record AdminCategoryResponse(int Id, string Name, string Slug, string? Description, int DisplayOrder, bool IsActive, int ProductCount, string? Color = null);

public record UpsertCategoryRequest(
    [Required, StringLength(100)] string Name,
    [Required, StringLength(120)] string Slug,
    [StringLength(500)] string? Description,
    [Range(0, int.MaxValue)] int DisplayOrder,
    string? Color = null);

public record UpdateCategoryStatusRequest(bool IsActive);
