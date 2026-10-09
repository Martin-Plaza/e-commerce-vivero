using System.Globalization;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Categories;
using GymShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Categories;

public interface IGetAdminCategoriesUseCase { Task<List<AdminCategoryResponse>> ExecuteAsync(CancellationToken cancellationToken = default); }
public interface IGetAdminCategoryByIdUseCase { Task<AppResult<AdminCategoryResponse>> ExecuteAsync(int id, CancellationToken cancellationToken = default); }
public interface ICreateCategoryUseCase { Task<AppResult<AdminCategoryResponse>> ExecuteAsync(UpsertCategoryRequest request, CancellationToken cancellationToken = default); }
public interface IUpdateCategoryUseCase { Task<AppResult<AdminCategoryResponse>> ExecuteAsync(int id, UpsertCategoryRequest request, CancellationToken cancellationToken = default); }
public interface IUpdateCategoryStatusUseCase { Task<AppResult> ExecuteAsync(int id, UpdateCategoryStatusRequest request, CancellationToken cancellationToken = default); }

internal static class CategoryRules
{
    private static readonly HashSet<string> Colors = new(StringComparer.OrdinalIgnoreCase) { "#d7ff45", "#ff8a5b", "#9f8cff", "#57d7ff", "#ff7eb6", "#77d8a3" };
    public static string NormalizeSlug(string value)
    {
        var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder();
        var separator = false;
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsAsciiLetterOrDigit(character)) { result.Append(character); separator = false; }
            else if (!separator && result.Length > 0) { result.Append('-'); separator = true; }
        }
        return result.ToString().Trim('-');
    }

    public static string? Validate(UpsertCategoryRequest request, string slug)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "El nombre es obligatorio.";
        if (request.Name.Trim().Length > 100) return "El nombre no puede superar los 100 caracteres.";
        if (string.IsNullOrEmpty(slug)) return "El slug debe contener letras o números.";
        if (slug.Length > 120) return "El slug no puede superar los 120 caracteres.";
        if (request.Description?.Trim().Length > 500) return "La descripción no puede superar los 500 caracteres.";
        if (request.DisplayOrder < 0) return "El orden de presentación debe ser mayor o igual a cero.";
        if (request.Color is not null && !Colors.Contains(request.Color)) return "Seleccioná un color disponible.";
        return null;
    }

    public static AdminCategoryResponse Map(Category category, int count) =>
        new(category.Id, category.Name, category.Slug, category.Description, category.DisplayOrder, category.IsActive, count, category.Color);
}

public class GetAdminCategoriesUseCase(IApplicationDbContext db) : IGetAdminCategoriesUseCase
{
    public Task<List<AdminCategoryResponse>> ExecuteAsync(CancellationToken cancellationToken = default) => db.Categories.AsNoTracking()
        .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
        .Select(x => new AdminCategoryResponse(x.Id, x.Name, x.Slug, x.Description, x.DisplayOrder, x.IsActive, x.Products.Count, x.Color))
        .ToListAsync(cancellationToken);
}

public class GetAdminCategoryByIdUseCase(IApplicationDbContext db) : IGetAdminCategoryByIdUseCase
{
    public async Task<AppResult<AdminCategoryResponse>> ExecuteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new AdminCategoryResponse(x.Id, x.Name, x.Slug, x.Description, x.DisplayOrder, x.IsActive, x.Products.Count, x.Color))
            .SingleOrDefaultAsync(cancellationToken);
        return category is null ? AppResult<AdminCategoryResponse>.Failure(AppErrorType.NotFound, "Categoría no encontrada.") : AppResult<AdminCategoryResponse>.Success(category);
    }
}

public class CreateCategoryUseCase(IApplicationDbContext db, IAuditContext? auditContext = null) : ICreateCategoryUseCase
{
    public async Task<AppResult<AdminCategoryResponse>> ExecuteAsync(UpsertCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var slug = CategoryRules.NormalizeSlug(request.Slug);
        var error = CategoryRules.Validate(request, slug);
        if (error is not null) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Validation, error);
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name.ToLower() == name.ToLower(), cancellationToken)) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Conflict, "Ya existe una categoría con ese nombre.");
        if (await db.Categories.AnyAsync(x => x.Slug == slug, cancellationToken)) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Conflict, "Ya existe una categoría con ese slug.");
        var category = new Category { Name = name, Slug = slug, Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(), Color = request.Color, DisplayOrder = request.DisplayOrder, IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        AuditTrail.Add(db, auditContext, "CategoryCreated", "Category", category.Id, null, new { category.Name, category.Slug, category.Description, category.Color, category.DisplayOrder, category.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<AdminCategoryResponse>.Success(CategoryRules.Map(category, 0));
    }
}

public class UpdateCategoryUseCase(IApplicationDbContext db, IAuditContext? auditContext = null) : IUpdateCategoryUseCase
{
    public async Task<AppResult<AdminCategoryResponse>> ExecuteAsync(int id, UpsertCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.Include(x => x.Products).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (category is null) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.NotFound, "Categoría no encontrada.");
        var slug = CategoryRules.NormalizeSlug(request.Slug);
        var error = CategoryRules.Validate(request, slug);
        if (error is not null) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Validation, error);
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Id != id && x.Name.ToLower() == name.ToLower(), cancellationToken)) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Conflict, "Ya existe una categoría con ese nombre.");
        if (await db.Categories.AnyAsync(x => x.Id != id && x.Slug == slug, cancellationToken)) return AppResult<AdminCategoryResponse>.Failure(AppErrorType.Conflict, "Ya existe una categoría con ese slug.");
        var oldValue = new { category.Name, category.Slug, category.Description, category.Color, category.DisplayOrder };
        category.Name = name; category.Slug = slug; category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(); category.Color = request.Color; category.DisplayOrder = request.DisplayOrder;
        AuditTrail.Add(db, auditContext, "CategoryUpdated", "Category", category.Id, oldValue, new { category.Name, category.Slug, category.Description, category.Color, category.DisplayOrder });
        await db.SaveChangesAsync(cancellationToken);
        return AppResult<AdminCategoryResponse>.Success(CategoryRules.Map(category, category.Products.Count));
    }
}

public class UpdateCategoryStatusUseCase(IApplicationDbContext db, IAuditContext? auditContext = null) : IUpdateCategoryStatusUseCase
{
    public async Task<AppResult> ExecuteAsync(int id, UpdateCategoryStatusRequest request, CancellationToken cancellationToken = default)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (category is null) return AppResult.Failure(AppErrorType.NotFound, "Categoría no encontrada.");
        if (category.IsActive == request.IsActive) return AppResult.Success();
        var previous = category.IsActive; category.IsActive = request.IsActive;
        AuditTrail.Add(db, auditContext, "CategoryStatusChanged", "Category", category.Id, new { IsActive = previous }, new { category.IsActive });
        await db.SaveChangesAsync(cancellationToken);
        return AppResult.Success();
    }
}
