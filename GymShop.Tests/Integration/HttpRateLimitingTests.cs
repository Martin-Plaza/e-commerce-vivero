using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HttpRateLimitingTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new(new Dictionary<string, string?>
    {
        ["RateLimiting:Enabled"] = "true",
        ["RateLimiting:LoginIp:PermitLimit"] = "2",
        ["RateLimiting:LoginIp:WindowSeconds"] = "60",
        ["RateLimiting:LoginAccount:PermitLimit"] = "2",
        ["RateLimiting:LoginAccount:WindowSeconds"] = "60"
    });
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateHttpsClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Exceeding_real_login_account_policy_returns_429_problem_details_and_retry_after()
    {
        var request = new { email = "unknown@test.com", password = "clave123" };
        var first = await _client.PostAsJsonAsync("/api/auth/login", request);
        var second = await _client.PostAsJsonAsync("/api/auth/login", request);
        var rejected = await _client.PostAsJsonAsync("/api/auth/login", request);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(429, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }
}
