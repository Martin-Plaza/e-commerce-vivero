using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Http")]
public sealed class CouponsAuthorizationHttpTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new(useInMemoryDatabase: true);
    private HttpClient _client = null!;
    public async Task InitializeAsync() { await _factory.InitializeAsync(); _client = _factory.CreateHttpsClient(); await _factory.SeedUserAsync("coupon-user@test.com", "User"); await _factory.SeedUserAsync("coupon-admin@test.com", "Admin"); await _factory.SeedUserAsync("coupon-super@test.com", "SuperAdmin"); }
    public async Task DisposeAsync() { _client.Dispose(); await ((IAsyncLifetime)_factory).DisposeAsync(); }

    [Fact]
    public async Task Anonymous_is_401_and_user_is_403_for_list_create_and_status()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/coupons")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/coupons", Payload("ANON"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.PatchAsJsonAsync("/api/coupons/1/status", new { isActive = false })).StatusCode);
        await Authorize("coupon-user@test.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/coupons")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/coupons", Payload("USER"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PatchAsJsonAsync("/api/coupons/1/status", new { isActive = false })).StatusCode);
    }

    [Theory]
    [InlineData("coupon-admin@test.com", "ADMIN")]
    [InlineData("coupon-super@test.com", "SUPER")]
    public async Task Admin_and_superadmin_can_list_create_and_change_status(string email, string code)
    {
        await Authorize(email);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/coupons?page=1&pageSize=10")).StatusCode);
        var created = await _client.PostAsJsonAsync("/api/coupons", Payload(code));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await _client.PatchAsJsonAsync($"/api/coupons/{id}/status", new { isActive = false })).StatusCode);
    }

    private async Task Authorize(string email) => _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.LoginAsync(_client, email));
    private static object Payload(string code) => new { code, name = code, type = "Percentage", value = 10m, minimumPurchase = (decimal?)null, maximumDiscount = (decimal?)null, startsAtUtc = (DateTime?)null, endsAtUtc = (DateTime?)null, totalUsageLimit = (int?)null, usageLimitPerUser = (int?)null, isActive = true };
}
