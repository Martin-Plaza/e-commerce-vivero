using GymShop.Application.DTOs.Dashboard;
using GymShop.Application.UseCases.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IGetDashboardStatisticsUseCase _statistics;

    public DashboardController(IGetDashboardStatisticsUseCase statistics) => _statistics = statistics;

    [HttpGet]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DashboardResponse>> Get([FromQuery] DashboardQueryRequest request, CancellationToken cancellationToken) =>
        FromResult(await _statistics.ExecuteAsync(request, cancellationToken));
}
