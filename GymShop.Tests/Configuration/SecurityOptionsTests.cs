using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Configuration;

public sealed class SecurityOptionsTests
{
    private readonly JwtOptionsValidator _jwtValidator = new();

    [Fact]
    public void Jwt_placeholder_is_rejected()
    {
        var result = ValidateJwt(JwtOptions.PlaceholderSecret);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Jwt_missing_secret_is_rejected(string? secret)
    {
        var result = ValidateJwt(secret);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Jwt_weak_secret_is_rejected()
    {
        var result = ValidateJwt("too-short-secret");

        Assert.True(result.Failed);
    }

    [Fact]
    public void Jwt_valid_secret_is_accepted()
    {
        var result = ValidateJwt("a-development-secret-with-at-least-32-characters");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Jwt_week_long_access_token_is_rejected()
    {
        var result = _jwtValidator.Validate(null, new JwtOptions
        {
            Secret = "a-development-secret-with-at-least-32-characters",
            ExpirationMinutes = 10080
        });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Production_enabled_mercado_pago_requires_webhook_secret()
    {
        var validator = new MercadoPagoOptionsValidator("Production");

        var result = validator.Validate(null, new MercadoPagoOptions { Enabled = true, AccessToken = "test-access-token" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Disabled_mercado_pago_allows_mock_without_mercado_pago_secrets()
    {
        var validator = new MercadoPagoOptionsValidator("Production");
        var options = new MercadoPagoOptions { Enabled = false };

        var result = validator.Validate(null, options);
        var gateway = new MercadoPagoPaymentGateway(new HttpClient(), Options.Create(options));

        Assert.True(result.Succeeded);
        Assert.False(gateway.CanHandle("MercadoPago"));
        Assert.True(new MockPaymentGateway().CanHandle("Mock"));
    }

    [Fact]
    public void Development_explicitly_allows_enabled_mercado_pago_without_webhook_secret()
    {
        var validator = new MercadoPagoOptionsValidator("Development");

        var result = validator.Validate(null, new MercadoPagoOptions { Enabled = true, AccessToken = "test-access-token" });

        Assert.True(result.Succeeded);
    }

    private ValidateOptionsResult ValidateJwt(string? secret) =>
        _jwtValidator.Validate(null, new JwtOptions { Secret = secret });
}
