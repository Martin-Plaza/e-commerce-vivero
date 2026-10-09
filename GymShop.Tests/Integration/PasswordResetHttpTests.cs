using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GymShop.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
public sealed class PasswordResetHttpTests
{
    [Fact]
    public async Task Non_local_registration_response_omits_verification_code()
    {
        var sender = new CapturingVerificationSender();
        await using var factory = new GymShopWebApplicationFactory(configureServices: services =>
        {
            services.RemoveAll<IVerificationEmailSender>();
            services.AddSingleton<IVerificationEmailSender>(sender);
        }, useInMemoryDatabase: true);
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            name = "Nueva",
            lastName = "Persona",
            email = "deployed-registration@test.com",
            password = "Clave1234"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("developmentCode", out _));
        Assert.Matches("^[0-9]{6}$", sender.Code!);
    }

    [Fact]
    public async Task Reset_password_uses_generic_response_and_invalidates_previous_jwt()
    {
        var sender = new CapturingPasswordResetSender();
        await using var factory = new GymShopWebApplicationFactory(configureServices: services =>
        {
            services.RemoveAll<IPasswordResetEmailSender>();
            services.AddSingleton<IPasswordResetEmailSender>(sender);
        }, useInMemoryDatabase: true);
        await factory.InitializeAsync();
        using var client = factory.CreateHttpsClient();
        const string email = "password-reset@test.com";
        await factory.SeedUserAsync(email, "User", "clave123");
        var previousToken = await factory.LoginAsync(client, email, "clave123");

        var existingResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        var unknownResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = "unknown@test.com" });
        Assert.Equal(HttpStatusCode.OK, existingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownResponse.StatusCode);
        using var existingJson = JsonDocument.Parse(await existingResponse.Content.ReadAsStringAsync());
        using var unknownJson = JsonDocument.Parse(await unknownResponse.Content.ReadAsStringAsync());
        Assert.Equal(existingJson.RootElement.GetProperty("message").GetString(), unknownJson.RootElement.GetProperty("message").GetString());
        Assert.Equal(600, existingJson.RootElement.GetProperty("expiresInSeconds").GetInt32());
        Assert.False(existingJson.RootElement.TryGetProperty("developmentCode", out _));
        Assert.False(unknownJson.RootElement.TryGetProperty("developmentCode", out _));
        var code = sender.Code;
        Assert.Matches("^[0-9]{6}$", code!);

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password", new { email, code, newPassword = "nuevaClave456" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", previousToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "clave123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "nuevaClave456" })).StatusCode);
    }

    private sealed class CapturingPasswordResetSender : IPasswordResetEmailSender
    {
        public string? Code { get; private set; }

        public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
        {
            if (deliver) Code = code;
            return Task.FromResult(EmailSendResult.Accepted());
        }
    }

    private sealed class CapturingVerificationSender : IVerificationEmailSender
    {
        public string? Code { get; private set; }

        public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
        {
            Code = code;
            return Task.FromResult(EmailSendResult.Accepted());
        }
    }
}
