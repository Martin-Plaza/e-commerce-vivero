using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.DTOs.Attributes;

public record AttributeOptionResponse(int Id, string Value, string? VisualValue, int DisplayOrder, bool IsActive, int UsageCount);
public record AttributeResponse(int Id, string Name, string Presentation, int DisplayOrder, bool IsActive, List<AttributeOptionResponse> Options);
public record UpsertAttributeRequest([Required, StringLength(100)] string Name, [Required] string Presentation, int DisplayOrder);
public record UpsertAttributeOptionRequest([Required, StringLength(100)] string Value, [StringLength(100)] string? VisualValue, int DisplayOrder);
public record UpdateAttributeStatusRequest(bool IsActive);
