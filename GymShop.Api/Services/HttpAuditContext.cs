using System.Security.Claims;
using GymShop.Application.Abstractions;

namespace GymShop.Api.Services;

public sealed class HttpAuditContext : IAuditContext
{
    private readonly IHttpContextAccessor _accessor;
    public HttpAuditContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public int? ActorUserId
    {
        get
        {
            var value = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public string CorrelationId => _accessor.HttpContext?.TraceIdentifier ?? $"system-{Guid.NewGuid():N}";
}
