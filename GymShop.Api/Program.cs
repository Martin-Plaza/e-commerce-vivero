using System.Security.Claims;
using System.Text;
using System.Net;
using System.Threading.RateLimiting;
using GymShop.Api.Configuration;
using GymShop.Api.Middleware;
using GymShop.Api.RateLimiting;
using GymShop.Api.Services;
using GymShop.Api.Security;
using GymShop.Application;
using GymShop.Application.Abstractions;
using GymShop.Application.UseCases.Payments;
using GymShop.Infrastructure;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGymShopRequestLimiter, GymShopRequestLimiter>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Ingresar solo el token JWT, sin el prefijo Bearer. La sesion se invalida si el usuario se desactiva o cambia de rol."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer", document, null),
            []
        }
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddApplication();
var storeTimeZone = new StoreTimeZone(builder.Configuration["Store:TimeZone"]);
builder.Services.AddSingleton<IStoreTimeZone>(storeTimeZone);
builder.Services.AddSingleton(PaymentCreationPolicy.FromSeconds(
    builder.Configuration.GetValue("Payments:CreatingTimeoutSeconds", 300)));
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IAuditContext, HttpAuditContext>();
builder.Services.AddScoped<JwtTokenValidationEvents>();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<MercadoPagoOptions>>(
    new MercadoPagoOptionsValidator(builder.Environment.EnvironmentName));
builder.Services.AddSingleton<IValidateOptions<GymShopRateLimitingOptions>>(
    new GymShopRateLimitingOptionsValidator(builder.Environment.EnvironmentName));
builder.Services.AddSingleton<IValidateOptions<ReverseProxyOptions>, ReverseProxyOptionsValidator>();
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<MercadoPagoOptions>()
    .Bind(builder.Configuration.GetSection(MercadoPagoOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<GymShopRateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(GymShopRateLimitingOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ReverseProxyOptions>()
    .Bind(builder.Configuration.GetSection(ReverseProxyOptions.SectionName))
    .ValidateOnStart();
var migrateOnly = args.Any(argument => string.Equals(argument, "--migrate-only", StringComparison.OrdinalIgnoreCase));
var initializeOnly = args.Any(argument => string.Equals(argument, "--initialize-only", StringComparison.OrdinalIgnoreCase));
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment, migrateOnly || initializeOnly);

var rateLimiting = builder.Configuration.GetSection(GymShopRateLimitingOptions.SectionName).Get<GymShopRateLimitingOptions>() ?? new();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? retry
            : TimeSpan.FromSeconds(1);
        context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Demasiadas solicitudes.",
            Detail = "Se alcanzo el limite temporal de solicitudes. Intenta nuevamente mas tarde.",
            Extensions = { ["traceId"] = context.HttpContext.TraceIdentifier }
        }, cancellationToken);
    };

    AddIpPolicy(options, RateLimitPolicies.LoginIp, rateLimiting.Enabled, rateLimiting.LoginIp);
    AddIpPolicy(options, RateLimitPolicies.RegistrationIp, rateLimiting.Enabled, rateLimiting.RegistrationIp);
    AddIpPolicy(options, RateLimitPolicies.PasswordResetIp, rateLimiting.Enabled, rateLimiting.LoginIp);
    AddIpPolicy(options, RateLimitPolicies.WebhookIp, rateLimiting.Enabled, rateLimiting.WebhookIp);
});

var reverseProxy = builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new();
if (reverseProxy.Enabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = reverseProxy.ForwardLimit;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in reverseProxy.KnownProxies)
        {
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });
}

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(BrowserSessionSecurity.CsrfHeader)
            .AllowCredentials();
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        options.EventsType = typeof(JwtTokenValidationEvents);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret!)),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (migrateOnly)
{
    await DatabaseInitializer.MigrateAsync(app.Services);
    return;
}

if (initializeOnly)
{
    await DatabaseInitializer.InitializeAsync(app.Services);
    return;
}

if (storeTimeZone.IsFallback)
{
    app.Logger.LogWarning("Store:TimeZone is invalid. Dashboard date calculations are falling back to UTC.");
}

_ = app.Services.GetRequiredService<IOptions<GymShopRateLimitingOptions>>().Value;
_ = app.Services.GetRequiredService<IOptions<ReverseProxyOptions>>().Value;

var mercadoPagoOptions = app.Services.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
if (app.Environment.IsDevelopment() && mercadoPagoOptions.Enabled && string.IsNullOrWhiteSpace(mercadoPagoOptions.WebhookSecret))
{
    app.Logger.LogWarning("Mercado Pago is enabled without webhook HMAC verification in Development. Configure MercadoPago:WebhookSecret before using real notifications.");
}

var initializeDatabase = builder.Configuration.GetValue(
    "DatabaseInitialization:Enabled",
    app.Environment.IsDevelopment());

if (initializeDatabase)
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (reverseProxy.Enabled)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    if (builder.Configuration.GetValue("Https:RedirectEnabled", true))
    {
        app.UseHttpsRedirection();
    }
}

app.UseExceptionHandler();
app.UseStaticFiles();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseMiddleware<BrowserCsrfMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

static void AddIpPolicy(RateLimiterOptions options, string name, bool enabled, RateLimitRule rule)
{
    options.AddPolicy(name, context => enabled
        ? RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => CreateFixedWindowOptions(rule))
        : RateLimitPartition.GetNoLimiter("disabled"));
}

static FixedWindowRateLimiterOptions CreateFixedWindowOptions(RateLimitRule rule) => new()
{
    PermitLimit = rule.PermitLimit,
    Window = TimeSpan.FromSeconds(rule.WindowSeconds),
    QueueLimit = 0,
    AutoReplenishment = true
};

public partial class Program;
