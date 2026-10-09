using GymShop.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Api.Middleware;

//este handler maneja las excepciones, viene de common.aplication

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validacion invalida"),
            NotFoundException => (StatusCodes.Status404NotFound, "Recurso no encontrado"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflicto"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Conflicto de concurrencia"),
            _ => (StatusCodes.Status500InternalServerError, "Error interno")
        };

        httpContext.Response.StatusCode = statusCode;
        var traceId = httpContext.TraceIdentifier;
        var isUnexpected = statusCode == StatusCodes.Status500InternalServerError;

        if (isUnexpected)
        {
            _logger.LogError(exception, "Unhandled exception while processing request. TraceId: {TraceId}", traceId);
        }

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = isUnexpected
                    ? "Ocurrio un error inesperado. Use el traceId para contactar al soporte."
                    : exception.Message,
                Extensions = { ["traceId"] = traceId }
            }
        });
    }
}
