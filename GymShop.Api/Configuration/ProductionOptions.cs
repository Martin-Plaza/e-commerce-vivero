using Microsoft.Extensions.Options;

namespace GymShop.Api.Configuration;

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
}

public sealed class GymShopRateLimitingOptions
{
    public const string SectionName = "RateLimiting";
    public bool Enabled { get; set; } = true;
    public RateLimitRule LoginIp { get; set; } = new();
    public RateLimitRule LoginAccount { get; set; } = new();
    public RateLimitRule RegistrationIp { get; set; } = new();
    public RateLimitRule RegistrationGlobal { get; set; } = new();
    public RateLimitRule PasswordResetAccount { get; set; } = new();
    public RateLimitRule PaymentUser { get; set; } = new();
    public RateLimitRule PaymentOrder { get; set; } = new();
    public RateLimitRule WebhookIp { get; set; } = new();
    public RateLimitRule WebhookGlobal { get; set; } = new();
}

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";
    public bool Enabled { get; set; }
    public int ForwardLimit { get; set; } = 1;
    public string[] KnownProxies { get; set; } = [];
}

public sealed class GymShopRateLimitingOptionsValidator : IValidateOptions<GymShopRateLimitingOptions>
{
    private readonly string _environmentName;

    public GymShopRateLimitingOptionsValidator(string environmentName) => _environmentName = environmentName;

    public ValidateOptionsResult Validate(string? name, GymShopRateLimitingOptions options)
    {
        if (string.Equals(_environmentName, "Production", StringComparison.OrdinalIgnoreCase) && !options.Enabled)
        {
            return ValidateOptionsResult.Fail("RateLimiting:Enabled must be true in Production.");
        }

        if (!options.Enabled) return ValidateOptionsResult.Success;

        var rules = new Dictionary<string, RateLimitRule>
        {
            [nameof(options.LoginIp)] = options.LoginIp,
            [nameof(options.LoginAccount)] = options.LoginAccount,
            [nameof(options.RegistrationIp)] = options.RegistrationIp,
            [nameof(options.RegistrationGlobal)] = options.RegistrationGlobal,
            [nameof(options.PasswordResetAccount)] = options.PasswordResetAccount,
            [nameof(options.PaymentUser)] = options.PaymentUser,
            [nameof(options.PaymentOrder)] = options.PaymentOrder,
            [nameof(options.WebhookIp)] = options.WebhookIp,
            [nameof(options.WebhookGlobal)] = options.WebhookGlobal
        };

        var invalid = rules.FirstOrDefault(x => x.Value.PermitLimit <= 0 || x.Value.WindowSeconds <= 0);
        return invalid.Value is null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"RateLimiting:{invalid.Key} requires positive PermitLimit and WindowSeconds.");
    }
}

public sealed class ReverseProxyOptionsValidator : IValidateOptions<ReverseProxyOptions>
{
    public ValidateOptionsResult Validate(string? name, ReverseProxyOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (options.ForwardLimit <= 0) return ValidateOptionsResult.Fail("ReverseProxy:ForwardLimit must be greater than zero.");
        if (options.KnownProxies.Length == 0) return ValidateOptionsResult.Fail("ReverseProxy requires at least one explicitly trusted KnownProxy when enabled.");
        if (options.KnownProxies.Any(x => !System.Net.IPAddress.TryParse(x, out _)))
        {
            return ValidateOptionsResult.Fail("ReverseProxy:KnownProxies contains an invalid IP address.");
        }

        return ValidateOptionsResult.Success;
    }
}
