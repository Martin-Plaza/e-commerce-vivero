using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string PlaceholderSecret = "SET_WITH_USER_SECRETS_OR_ENVIRONMENT";
    public const int MinimumSecretLength = 32;

    public string? Issuer { get; init; }
    public string? Audience { get; init; }
    public string? Secret { get; init; }
    public int ExpirationMinutes { get; init; } = 15;
    public int RefreshExpirationDays { get; init; } = 14;
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret))
        {
            return ValidateOptionsResult.Fail("Jwt:Secret is required. Configure it with User Secrets or an environment variable.");
        }

        if (string.Equals(options.Secret.Trim(), JwtOptions.PlaceholderSecret, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail("Jwt:Secret still contains the documented placeholder and must be replaced.");
        }

        if (options.Secret.Length < JwtOptions.MinimumSecretLength)
        {
            return ValidateOptionsResult.Fail($"Jwt:Secret must contain at least {JwtOptions.MinimumSecretLength} characters.");
        }

        if (options.ExpirationMinutes is < 5 or > 30)
            return ValidateOptionsResult.Fail("Jwt:ExpirationMinutes must be between 5 and 30 minutes.");

        if (options.RefreshExpirationDays is < 1 or > 30)
            return ValidateOptionsResult.Fail("Jwt:RefreshExpirationDays must be between 1 and 30 days.");

        return ValidateOptionsResult.Success;
    }
}

public sealed class MfaOptions
{
    public const string SectionName = "Mfa";
    public string Issuer { get; init; } = "GymShop";
    public string? EncryptionKey { get; init; }
    public int ChallengeMinutes { get; init; } = 5;
}

public sealed class MfaOptionsValidator : IValidateOptions<MfaOptions>
{
    public ValidateOptionsResult Validate(string? name, MfaOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || options.Issuer.Length > 50)
            return ValidateOptionsResult.Fail("Mfa:Issuer is required and cannot exceed 50 characters.");
        if (options.ChallengeMinutes is < 2 or > 10)
            return ValidateOptionsResult.Fail("Mfa:ChallengeMinutes must be between 2 and 10 minutes.");
        if (string.IsNullOrWhiteSpace(options.EncryptionKey))
            return ValidateOptionsResult.Fail("Mfa:EncryptionKey is required and must be a Base64-encoded 32-byte key.");
        try
        {
            if (Convert.FromBase64String(options.EncryptionKey).Length != 32)
                return ValidateOptionsResult.Fail("Mfa:EncryptionKey must decode to exactly 32 bytes.");
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail("Mfa:EncryptionKey must be valid Base64.");
        }
        return ValidateOptionsResult.Success;
    }
}

public sealed class MercadoPagoOptions
{
    public const string SectionName = "MercadoPago";

    public bool Enabled { get; init; }
    public string? AccessToken { get; init; }
    public string? PublicKey { get; init; }
    public string? WebhookSecret { get; init; }
    public string? NotificationUrl { get; init; }
    public string? SuccessUrl { get; init; }
    public string? FailureUrl { get; init; }
    public string? PendingUrl { get; init; }
    public string? CheckoutSuccessUrl { get; init; }
    public string? CheckoutFailureUrl { get; init; }
    public string? CheckoutPendingUrl { get; init; }
    public bool UseSandboxInitPoint { get; init; } = true;
    public int WebhookSignatureMaxAgeSeconds { get; init; } = 300;
}

public sealed class MercadoPagoOptionsValidator : IValidateOptions<MercadoPagoOptions>
{
    private readonly bool _isProduction;

    public MercadoPagoOptionsValidator(string environmentName)
    {
        _isProduction = string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
    }

    public ValidateOptionsResult Validate(string? name, MercadoPagoOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.AccessToken))
        {
            return ValidateOptionsResult.Fail("MercadoPago:AccessToken is required when Mercado Pago is enabled.");
        }

        if (options.Enabled && _isProduction && string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            return ValidateOptionsResult.Fail("MercadoPago:WebhookSecret is required when Mercado Pago is enabled in Production.");
        }

        if (options.WebhookSignatureMaxAgeSeconds is < 60 or > 900)
            return ValidateOptionsResult.Fail("MercadoPago:WebhookSignatureMaxAgeSeconds must be between 60 and 900 seconds.");

        return ValidateOptionsResult.Success;
    }
}
