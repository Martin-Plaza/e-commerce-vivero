using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using GymShop.Application.DTOs.Products;
using GymShop.Application.UseCases.Products;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class HttpPipelineTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateHttpsClient();
        await _factory.SeedUserAsync("user-http@test.com", "User");
        await _factory.SeedUserAsync("admin-http@test.com", "Admin");
        await _factory.SeedUserAsync("super-http@test.com", "SuperAdmin");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Login_serializes_user_without_token_and_sets_http_only_session_cookies()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "user-http@test.com", password = "clave123" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("token", out _));
        Assert.Equal("User", json.RootElement.GetProperty("user").GetProperty("role").GetString());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Contains(cookies, value => value.StartsWith("gymshop-access=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cookies, value => value.StartsWith("gymshop-refresh=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_and_invalid_jwt_return_401_while_insufficient_role_returns_403()
    {
        var missing = await _client.GetAsync("/api/audit");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        var invalid = await _client.GetAsync("/api/audit");
        var userToken = await _factory.LoginAsync(_factory.CreateHttpsClient(), "user-http@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var forbidden = await _client.GetAsync("/api/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Valid_superadmin_jwt_can_query_audit()
    {
        var token = await _factory.LoginAsync(_client, "super-http@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/audit?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task Invalid_registration_returns_validation_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new { name = "", email = "bad", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
        Assert.True(json.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task Public_catalog_hides_inactive_and_admin_can_get_it_by_id()
    {
        int inactiveId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GymShopDbContext>();
            db.Products.AddRange(
                new Product { Name = "Activo HTTP", Price = 10, Stock = 1, IsActive = true },
                new Product { Name = "Inactivo HTTP", Price = 10, Stock = 1, IsActive = false });
            await db.SaveChangesAsync();
            inactiveId = await db.Products.Where(x => !x.IsActive).Select(x => x.Id).SingleAsync();
        }

        var catalog = await _client.GetFromJsonAsync<List<ProductResponse>>("/api/products");
        var hidden = await _client.GetAsync($"/api/products/{inactiveId}");
        var forced = await _client.GetAsync("/api/products?includeInactive=true");
        var token = await _factory.LoginAsync(_client, "admin-http@test.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var admin = await _client.GetAsync($"/api/products/{inactiveId}");

        Assert.Single(catalog!);
        Assert.Equal("Activo HTTP", catalog![0].Name);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forced.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task Unexpected_exception_returns_generic_500_with_trace_id()
    {
        await using var failingFactory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGetProductsUseCase>();
            services.AddScoped<IGetProductsUseCase, ThrowingProductsUseCase>();
        }));
        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.GetAsync("/api/products");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("internal-http-secret", body);
        using var json = JsonDocument.Parse(body);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    private sealed class ThrowingProductsUseCase : IGetProductsUseCase
    {
        public Task<List<ProductResponse>> ExecuteAsync(ProductQuery query, bool canViewInactive, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("internal-http-secret");
    }
}
