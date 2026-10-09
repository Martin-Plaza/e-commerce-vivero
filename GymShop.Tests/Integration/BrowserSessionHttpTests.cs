using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class BrowserSessionHttpTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new(useInMemoryDatabase: true);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateHttpsClient();
        await _factory.SeedUserAsync("browser-session@test.com", "User");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Login_uses_http_only_cookies_and_does_not_serialize_jwt()
    {
        var response = await LoginAsync();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();

        Assert.False(body.TryGetProperty("token", out _));
        Assert.Equal("browser-session@test.com", body.GetProperty("user").GetProperty("email").GetString());
        Assert.Contains(cookies, value => value.StartsWith("gymshop-access=") && HasFlags(value, "httponly", "secure", "samesite=none", "partitioned"));
        Assert.Contains(cookies, value => value.StartsWith("gymshop-refresh=") && HasFlags(value, "httponly", "secure", "samesite=none", "partitioned"));
        Assert.Contains(cookies, value => value.StartsWith("XSRF-TOKEN=") && !value.Contains("httponly", StringComparison.OrdinalIgnoreCase) && HasFlags(value, "secure", "samesite=none", "partitioned"));
    }

    [Fact]
    public async Task Cookie_authenticated_mutation_requires_matching_csrf_header()
    {
        var login = await LoginAsync();
        var csrf = CookieValue(login, "XSRF-TOKEN");

        var rejected = await _client.PostAsync("/api/auth/logout", null);
        using var acceptedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        acceptedRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        var accepted = await _client.SendAsync(acceptedRequest);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    [Fact]
    public async Task Csrf_bootstrap_returns_matching_header_body_and_cookie()
    {
        var response = await _client.GetAsync("/api/auth/csrf");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.Equal(token, response.Headers.GetValues("X-CSRF-TOKEN").Single());
        Assert.Equal(token, CookieValue(response, "XSRF-TOKEN"));
    }

    private Task<HttpResponseMessage> LoginAsync() => _client.PostAsJsonAsync("/api/auth/login", new
    {
        email = "browser-session@test.com",
        password = "clave123"
    });

    private static string CookieValue(HttpResponseMessage response, string name)
    {
        var pair = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(pair[(name.Length + 1)..]);
    }

    private static bool HasFlags(string cookie, params string[] flags) =>
        flags.All(flag => cookie.Contains(flag, StringComparison.OrdinalIgnoreCase));
}
