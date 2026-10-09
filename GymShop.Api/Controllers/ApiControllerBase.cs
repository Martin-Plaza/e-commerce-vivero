using GymShop.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult FromResult(AppResult result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ToErrorResponse(result.Error!);
    }

    protected ActionResult<T> FromResult<T>(AppResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return ToErrorResponse(result.Error!);
    }

    protected ActionResult ToErrorResponse(AppError error)
    {
        var body = new { message = error.Message, code = error.Code };

        return error.Type switch
        {
            AppErrorType.Validation => BadRequest(body),
            AppErrorType.Unauthorized => Unauthorized(body),
            AppErrorType.Forbidden => Forbid(),
            AppErrorType.NotFound => NotFound(body),
            AppErrorType.Conflict => Conflict(body),
            AppErrorType.Unavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, body),
            _ => BadRequest(body)
        };
    }
}
