using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ApiControllerBase
{
    private readonly IGetProductsUseCase _getProducts;
    private readonly IGetProductByIdUseCase _getProductById;
    private readonly ICreateProductUseCase _createProduct;
    private readonly IUpdateProductUseCase _updateProduct;
    private readonly IUpdateProductStatusUseCase _updateProductStatus;

    public ProductsController(
        IGetProductsUseCase getProducts,
        IGetProductByIdUseCase getProductById,
        ICreateProductUseCase createProduct,
        IUpdateProductUseCase updateProduct,
        IUpdateProductStatusUseCase updateProductStatus)
    {
        _getProducts = getProducts;
        _getProductById = getProductById;
        _createProduct = createProduct;
        _updateProduct = updateProduct;
        _updateProductStatus = updateProductStatus;
    }


    [HttpGet]
    [ProducesResponseType(typeof(List<ProductResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]

    //get de todos los productos, y que no esten activos
    public async Task<ActionResult<List<ProductResponse>>> GetAll(
        [FromQuery] ProductQuery query,
        CancellationToken cancellationToken = default)
    {
        //variable para que admin y superadmin puedan ver los inactivos
        var canViewInactive = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");

        //si el usuario quiere quiere ver los inactivos pero no es admin ni superadmin este if valida si esta autenticado
        //si es verdadero usa forbid (que es sin autorizacion), si no esta autenticado avisa que necesita autenticacion.
        if (query.IncludeInactive && !canViewInactive)
        {
            return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
        }

        //retorna los productos
        return Ok(await _getProducts.ExecuteAsync(query, canViewInactive, cancellationToken));
    }


    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(int id, CancellationToken cancellationToken)
    {
        var canViewInactive = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        //llamada FromResult de ApiControllerBase
        return FromResult(await _getProductById.ExecuteAsync(id, canViewInactive, cancellationToken));
    }



    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPost]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var result = await _createProduct.ExecuteAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, result.Value)
            : ToErrorResponse(result.Error!);
    }


    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Update(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _updateProduct.ExecuteAsync(id, request, cancellationToken));
    }


    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult> UpdateStatus(int id, UpdateProductStatusRequest request, CancellationToken cancellationToken)
    {
        return FromResult(await _updateProductStatus.ExecuteAsync(id, request, cancellationToken));
    }
}
