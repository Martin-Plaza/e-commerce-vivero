using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Attributes;
using GymShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Attributes;

public interface IAttributeAdminService
{
    Task<List<AttributeResponse>> GetAsync(bool includeInactive, CancellationToken ct);
    Task<AppResult<AttributeResponse>> CreateAsync(UpsertAttributeRequest request, CancellationToken ct);
    Task<AppResult<AttributeResponse>> UpdateAsync(int id, UpsertAttributeRequest request, CancellationToken ct);
    Task<AppResult> SetStatusAsync(int id, bool active, CancellationToken ct);
    Task<AppResult<AttributeOptionResponse>> AddOptionAsync(int id, UpsertAttributeOptionRequest request, CancellationToken ct);
    Task<AppResult<AttributeOptionResponse>> UpdateOptionAsync(int id, int optionId, UpsertAttributeOptionRequest request, CancellationToken ct);
    Task<AppResult> SetOptionStatusAsync(int id, int optionId, bool active, CancellationToken ct);
}

public class AttributeAdminService(IApplicationDbContext db) : IAttributeAdminService
{
    public async Task<List<AttributeResponse>> GetAsync(bool includeInactive, CancellationToken ct) => (await db.ProductAttributes.AsNoTracking().Include(x => x.Options).ThenInclude(x => x.VariantAttributes)
        .Where(x => includeInactive || x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name).ToListAsync(ct)).Select(Map).ToList();

    public async Task<AppResult<AttributeResponse>> CreateAsync(UpsertAttributeRequest r, CancellationToken ct)
    {
        var error = Validate(r); if (error is not null) return Fail<AttributeResponse>(error);
        if (await db.ProductAttributes.AnyAsync(x => x.Name.ToLower() == r.Name.Trim().ToLower(), ct)) return AppResult<AttributeResponse>.Failure(AppErrorType.Conflict, "Ya existe un atributo con ese nombre.");
        var entity = new ProductAttribute { Name = r.Name.Trim(), Presentation = r.Presentation, DisplayOrder = r.DisplayOrder };
        db.ProductAttributes.Add(entity); await db.SaveChangesAsync(ct); return AppResult<AttributeResponse>.Success(Map(entity));
    }
    public async Task<AppResult<AttributeResponse>> UpdateAsync(int id, UpsertAttributeRequest r, CancellationToken ct)
    {
        var entity = await db.ProductAttributes.Include(x => x.Options).ThenInclude(x => x.VariantAttributes).ThenInclude(x => x.ProductVariant).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return AppResult<AttributeResponse>.Failure(AppErrorType.NotFound, "Atributo no encontrado.");
        var error = Validate(r); if (error is not null) return Fail<AttributeResponse>(error);
        if (await db.ProductAttributes.AnyAsync(x => x.Id != id && x.Name.ToLower() == r.Name.Trim().ToLower(), ct)) return AppResult<AttributeResponse>.Failure(AppErrorType.Conflict, "Ya existe un atributo con ese nombre.");
        if (!entity.Name.Equals(r.Name.Trim(), StringComparison.OrdinalIgnoreCase) && entity.Options.Any(x => x.VariantAttributes.Count > 0))
            return AppResult<AttributeResponse>.Failure(AppErrorType.Conflict, "No se puede renombrar un atributo que ya está usado por variantes.");
        if (entity.Presentation != r.Presentation && entity.Options.Any(x => x.VariantAttributes.Count > 0))
            return AppResult<AttributeResponse>.Failure(AppErrorType.Conflict, "No se puede cambiar la presentación de un atributo que ya está usado por variantes.");
        entity.Name = r.Name.Trim(); entity.Presentation = r.Presentation; entity.DisplayOrder = r.DisplayOrder; await db.SaveChangesAsync(ct); return AppResult<AttributeResponse>.Success(Map(entity));
    }
    public async Task<AppResult> SetStatusAsync(int id, bool active, CancellationToken ct)
    {
        var entity = await db.ProductAttributes.Include(x => x.Options).ThenInclude(x => x.VariantAttributes).ThenInclude(x => x.ProductVariant).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return AppResult.Failure(AppErrorType.NotFound, "Atributo no encontrado.");
        if (!active && entity.Options.Any(x => x.VariantAttributes.Any(v => v.ProductVariant.IsActive))) return AppResult.Failure(AppErrorType.Conflict, "No se puede desactivar un atributo usado por variantes activas.");
        entity.IsActive = active; await db.SaveChangesAsync(ct); return AppResult.Success();
    }
    public async Task<AppResult<AttributeOptionResponse>> AddOptionAsync(int id, UpsertAttributeOptionRequest r, CancellationToken ct)
    {
        var attr = await db.ProductAttributes.Include(x => x.Options).SingleOrDefaultAsync(x => x.Id == id, ct); if (attr is null) return AppResult<AttributeOptionResponse>.Failure(AppErrorType.NotFound, "Atributo no encontrado.");
        var error = ValidateOption(attr, r); if (error is not null) return Fail<AttributeOptionResponse>(error);
        if (attr.Options.Any(x => x.Value.Equals(r.Value.Trim(), StringComparison.OrdinalIgnoreCase))) return AppResult<AttributeOptionResponse>.Failure(AppErrorType.Conflict, "La opción ya existe.");
        var option = new ProductAttributeOption { Value = r.Value.Trim(), VisualValue = Clean(r.VisualValue), DisplayOrder = r.DisplayOrder }; attr.Options.Add(option); await db.SaveChangesAsync(ct); return AppResult<AttributeOptionResponse>.Success(Map(option));
    }
    public async Task<AppResult<AttributeOptionResponse>> UpdateOptionAsync(int id, int optionId, UpsertAttributeOptionRequest r, CancellationToken ct)
    {
        var attr = await db.ProductAttributes.Include(x => x.Options).ThenInclude(x => x.VariantAttributes).SingleOrDefaultAsync(x => x.Id == id, ct); if (attr is null) return AppResult<AttributeOptionResponse>.Failure(AppErrorType.NotFound, "Atributo no encontrado.");
        var option = attr.Options.SingleOrDefault(x => x.Id == optionId); if (option is null) return AppResult<AttributeOptionResponse>.Failure(AppErrorType.NotFound, "Opción no encontrada.");
        var error = ValidateOption(attr, r); if (error is not null) return Fail<AttributeOptionResponse>(error);
        if (attr.Options.Any(x => x.Id != optionId && x.Value.Equals(r.Value.Trim(), StringComparison.OrdinalIgnoreCase))) return AppResult<AttributeOptionResponse>.Failure(AppErrorType.Conflict, "La opción ya existe.");
        if (!option.Value.Equals(r.Value.Trim(), StringComparison.OrdinalIgnoreCase) && option.VariantAttributes.Count > 0)
            return AppResult<AttributeOptionResponse>.Failure(AppErrorType.Conflict, "No se puede renombrar una opción que ya está usada por variantes.");
        option.Value = r.Value.Trim(); option.VisualValue = Clean(r.VisualValue); option.DisplayOrder = r.DisplayOrder; await db.SaveChangesAsync(ct); return AppResult<AttributeOptionResponse>.Success(Map(option));
    }
    public async Task<AppResult> SetOptionStatusAsync(int id, int optionId, bool active, CancellationToken ct)
    {
        var option = await db.ProductAttributeOptions.Include(x => x.VariantAttributes).ThenInclude(x => x.ProductVariant).SingleOrDefaultAsync(x => x.Id == optionId && x.ProductAttributeId == id, ct);
        if (option is null) return AppResult.Failure(AppErrorType.NotFound, "Opción no encontrada.");
        if (!active && option.VariantAttributes.Any(x => x.ProductVariant.IsActive)) return AppResult.Failure(AppErrorType.Conflict, "No se puede desactivar una opción usada por variantes activas.");
        option.IsActive = active; await db.SaveChangesAsync(ct); return AppResult.Success();
    }
    private static string? Validate(UpsertAttributeRequest r) => string.IsNullOrWhiteSpace(r.Name) ? "El nombre es obligatorio." : r.Presentation is not ("Button" or "ColorSwatch") ? "La presentación debe ser Button o ColorSwatch." : r.DisplayOrder < 0 ? "El orden no puede ser negativo." : null;
    private static string? ValidateOption(ProductAttribute a, UpsertAttributeOptionRequest r) => string.IsNullOrWhiteSpace(r.Value) ? "El valor es obligatorio." : r.DisplayOrder < 0 ? "El orden no puede ser negativo." : a.Presentation == "ColorSwatch" && string.IsNullOrWhiteSpace(r.VisualValue) ? "Una muestra de color necesita un color visual." : null;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AttributeResponse Map(ProductAttribute x) => new(x.Id, x.Name, x.Presentation, x.DisplayOrder, x.IsActive, x.Options.OrderBy(o => o.DisplayOrder).ThenBy(o => o.Value).Select(Map).ToList());
    private static AttributeOptionResponse Map(ProductAttributeOption x) => new(x.Id, x.Value, x.VisualValue, x.DisplayOrder, x.IsActive, x.VariantAttributes.Count);
    private static AppResult<T> Fail<T>(string error) => AppResult<T>.Failure(AppErrorType.Validation, error);
}
