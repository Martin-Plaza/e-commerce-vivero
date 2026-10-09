using GymShop.Application.DTOs.Categories;
using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Categories;
using GymShop.Application.UseCases.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(
    IGetCategoriesUseCase getCategories,
    IGetAdminCategoriesUseCase getAdminCategories,
    IGetAdminCategoryByIdUseCase getAdminCategory,
    ICreateCategoryUseCase createCategory,
    IUpdateCategoryUseCase updateCategory,
    IUpdateCategoryStatusUseCase updateCategoryStatus) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(List<CategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<CategoryResponse>>> GetAll(CancellationToken cancellationToken) =>
        Ok(await getCategories.ExecuteAsync(cancellationToken));

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("admin")]
    public async Task<ActionResult<List<AdminCategoryResponse>>> GetAdmin(CancellationToken cancellationToken) => Ok(await getAdminCategories.ExecuteAsync(cancellationToken));

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AdminCategoryResponse>> GetById(int id, CancellationToken cancellationToken) => FromResult(await getAdminCategory.ExecuteAsync(id, cancellationToken));

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost]
    public async Task<ActionResult<AdminCategoryResponse>> Create(UpsertCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await createCategory.ExecuteAsync(request, cancellationToken);
        return result.IsSuccess ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value) : ToErrorResponse(result.Error!);
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<AdminCategoryResponse>> Update(int id, UpsertCategoryRequest request, CancellationToken cancellationToken) => FromResult(await updateCategory.ExecuteAsync(id, request, cancellationToken));

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, UpdateCategoryStatusRequest request, CancellationToken cancellationToken) => FromResult(await updateCategoryStatus.ExecuteAsync(id, request, cancellationToken));
}
