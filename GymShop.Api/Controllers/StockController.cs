using GymShop.Application.DTOs.Stock;
using GymShop.Application.UseCases.Stock;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/stock")]
public sealed class StockController(IGetStockMovementsUseCase getMovements, IAdjustStockUseCase adjustStock) : ApiControllerBase
{
    [HttpGet("movements")]
    public async Task<ActionResult<PagedStockMovementsResponse>> GetMovements([FromQuery] StockMovementQuery request, CancellationToken cancellationToken) =>
        FromResult(await getMovements.ExecuteAsync(request, cancellationToken));

    [HttpGet("products/{productId:int}/movements")]
    public async Task<ActionResult<PagedStockMovementsResponse>> GetProductMovements(int productId, [FromQuery] StockMovementQuery request, CancellationToken cancellationToken) =>
        FromResult(await getMovements.ExecuteAsync(request with { ProductId = productId }, cancellationToken));

    [HttpPost("products/{productId:int}/adjustments")]
    public async Task<ActionResult<StockAdjustmentResponse>> Adjust(int productId, ManualStockAdjustmentRequest request, CancellationToken cancellationToken) =>
        FromResult(await adjustStock.ExecuteAsync(productId, request, cancellationToken));
}
