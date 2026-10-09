using GymShop.Application.DTOs.Audit;
using GymShop.Application.UseCases.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

[ApiController]
[Authorize(Roles = "SuperAdmin")]
[Route("api/audit")]
public sealed class AuditController : ApiControllerBase
{
    private readonly IGetAuditEntriesUseCase _getAuditEntries;
    public AuditController(IGetAuditEntriesUseCase getAuditEntries) => _getAuditEntries = getAuditEntries;

    [HttpGet]
    [ProducesResponseType(typeof(PagedAuditResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedAuditResponse>> Get([FromQuery] AuditQueryRequest request, CancellationToken cancellationToken) =>
        FromResult(await _getAuditEntries.ExecuteAsync(request, cancellationToken));
}
