using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class UsersAuthorizationHttpTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new();
    private HttpClient _client = null!;
    public async Task InitializeAsync() { await _factory.InitializeAsync(); _client = _factory.CreateHttpsClient(); await _factory.SeedUserAsync("user-users@test.com", "User"); await _factory.SeedUserAsync("admin-users@test.com", "Admin"); await _factory.SeedUserAsync("super-users@test.com", "SuperAdmin"); }
    public async Task DisposeAsync() { _client.Dispose(); await ((IAsyncLifetime)_factory).DisposeAsync(); }

    [Fact]
    public async Task Anonymous_is_401_user_and_admin_are_403_and_superadmin_can_list()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/users")).StatusCode);
        foreach (var email in new[] { "user-users@test.com", "admin-users@test.com" }) { var token = await _factory.LoginAsync(_client, email); _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/users")).StatusCode); }
        var super = await _factory.LoginAsync(_client, "super-users@test.com"); _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", super); Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/users?page=1&pageSize=10")).StatusCode);
    }

    [Fact]
    public async Task Only_superadmin_can_create_change_role_and_status()
    {
        foreach (var email in new[] { "user-users@test.com", "admin-users@test.com" }) { var token = await _factory.LoginAsync(_client, email); _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/users", new { name = "Denied", email = $"denied-{email}@test.com", password = "123456", role = "User" })).StatusCode); }
        var super = await _factory.LoginAsync(_client, "super-users@test.com"); _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", super);
        var created = await _client.PostAsJsonAsync("/api/users", new { name = "Managed", email = "managed@test.com", password = "123456", role = "User" }); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); var id = body.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PatchAsJsonAsync($"/api/users/{id}/role", new { role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PatchAsJsonAsync($"/api/users/{id}/status", new { isActive = false })).StatusCode);
    }
}
