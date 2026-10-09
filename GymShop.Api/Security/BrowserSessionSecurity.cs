using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Api.Security;

public static class BrowserSessionSecurity
{
    public const string AccessCookie = "gymshop-access";
    public const string RefreshCookie = "gymshop-refresh";
    public const string MfaChallengeCookie = "gymshop-mfa";
    public const string CsrfCookie = "XSRF-TOKEN";
    public const string CsrfHeader = "X-CSRF-TOKEN";

    public static string CreateCsrfToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}

public sealed class BrowserCsrfMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Get, HttpMethods.Head, HttpMethods.Options, HttpMethods.Trace
    };
    private static readonly HashSet<string> PublicAuthenticationPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/register",
        "/api/auth/login",
        "/api/auth/verify-email",
        "/api/auth/resend-verification",
        "/api/auth/forgot-password",
        "/api/auth/reset-password"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresValidation(context) && !IsValid(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Solicitud no válida.",
                Detail = "Falta el token de protección CSRF o no coincide.",
                Extensions = { ["traceId"] = context.TraceIdentifier }
            });
            return;
        }

        await next(context);
    }

    private static bool RequiresValidation(HttpContext context)
    {
        if (SafeMethods.Contains(context.Request.Method)) return false;
        if (context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        var path = context.Request.Path;
        if (PublicAuthenticationPaths.Contains(path.Value ?? string.Empty)) return false;
        if (path.StartsWithSegments("/api/auth/refresh") || path.StartsWithSegments("/api/auth/logout"))
            return context.Request.Cookies.ContainsKey(BrowserSessionSecurity.RefreshCookie);
        if (path.StartsWithSegments("/api/auth/mfa"))
            return context.Request.Cookies.ContainsKey(BrowserSessionSecurity.MfaChallengeCookie);

        return context.User.Identity?.IsAuthenticated == true &&
               context.Request.Cookies.ContainsKey(BrowserSessionSecurity.AccessCookie);
    }

    private static bool IsValid(HttpContext context)
    {
        var cookie = context.Request.Cookies[BrowserSessionSecurity.CsrfCookie];
        var header = context.Request.Headers[BrowserSessionSecurity.CsrfHeader].ToString();
        return !string.IsNullOrWhiteSpace(cookie) && !string.IsNullOrWhiteSpace(header) &&
               BrowserSessionSecurity.FixedTimeEquals(cookie, header);
    }
}
