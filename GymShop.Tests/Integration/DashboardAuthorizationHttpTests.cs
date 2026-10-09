using System.Net;
using System.Net.Http.Headers;

namespace GymShop.Tests.Integration;

public sealed class DashboardAuthorizationHttpTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new(useInMemoryDatabase: true);
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = _factory.CreateHttpsClient();
        await _factory.SeedUserAsync("dashboard-user@gymshop.test", "User");
        await _factory.SeedUserAsync("dashboard-admin@gymshop.test", "Admin");
        await _factory.SeedUserAsync("dashboard-super@gymshop.test", "SuperAdmin");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await ((IAsyncLifetime)_factory).DisposeAsync();
    }

    [Fact]
    public async Task Dashboard_rejects_anonymous_and_user_but_allows_admin_roles()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/dashboard?period=7d")).StatusCode);

        await AuthenticateAsync("dashboard-user@gymshop.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/admin/dashboard?period=7d")).StatusCode);

        await AuthenticateAsync("dashboard-admin@gymshop.test");
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/dashboard?period=7d")).StatusCode);

        await AuthenticateAsync("dashboard-super@gymshop.test");
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/admin/dashboard?period=7d")).StatusCode);
    }

    private async Task AuthenticateAsync(string email) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.LoginAsync(_client, email));
}
