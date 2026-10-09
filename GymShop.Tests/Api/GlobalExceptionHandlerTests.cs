using System.Text.Json;
using GymShop.Api.Middleware;
using GymShop.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymShop.Tests.Api;

public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Unexpected_error_returns_generic_500_with_trace_id_and_logs_exception()
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var (handler, context, body) = CreateHandler(logger, "trace-test-123");
        var exception = new InvalidOperationException("PostgreSQL failed at C:\\internal\\secret-path");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);
        var json = await ReadJsonAsync(body);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("Error interno", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("trace-test-123", json.RootElement.GetProperty("traceId").GetString());
        Assert.DoesNotContain(exception.Message, json.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("error inesperado", json.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Same(exception, logger.Exception);
        Assert.Contains("trace-test-123", logger.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("validation", StatusCodes.Status400BadRequest, "Validacion invalida")]
    [InlineData("not-found", StatusCodes.Status404NotFound, "Recurso no encontrado")]
    [InlineData("conflict", StatusCodes.Status409Conflict, "Conflicto")]
    public async Task Controlled_errors_keep_status_and_useful_message(string kind, int expectedStatus, string expectedTitle)
    {
        var logger = new CapturingLogger<GlobalExceptionHandler>();
        var (handler, context, body) = CreateHandler(logger, "controlled-trace");
        const string domainMessage = "Mensaje util del dominio";
        Exception exception = kind switch
        {
            "validation" => new ValidationException(domainMessage),
            "not-found" => new NotFoundException(domainMessage),
            _ => new ConflictException(domainMessage)
        };

        await handler.TryHandleAsync(context, exception, CancellationToken.None);
        var json = await ReadJsonAsync(body);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal(expectedTitle, json.RootElement.GetProperty("title").GetString());
        Assert.Equal(domainMessage, json.RootElement.GetProperty("detail").GetString());
        Assert.Null(logger.Exception);
    }

    private static (GlobalExceptionHandler Handler, DefaultHttpContext Context, MemoryStream Body) CreateHandler(
        ILogger<GlobalExceptionHandler> logger,
        string traceId)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddProblemDetails()
            .BuildServiceProvider();
        var problemDetailsService = services.GetRequiredService<IProblemDetailsService>();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = traceId
        };
        var body = new MemoryStream();
        context.Response.Body = body;

        return (new GlobalExceptionHandler(problemDetailsService, logger), context, body);
    }

    private static async Task<JsonDocument> ReadJsonAsync(MemoryStream body)
    {
        body.Position = 0;
        return await JsonDocument.ParseAsync(body);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
