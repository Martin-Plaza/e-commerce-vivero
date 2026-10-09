using System.Net;
using System.Net.Http.Headers;
using GymShop.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GymShop.Tests.Integration;

public sealed class ProductImageAuthorizationHttpTests : IAsyncLifetime
{
    private readonly GymShopWebApplicationFactory _factory = new(
        configureServices: services =>
        {
            services.RemoveAll<IProductImageStorage>();
            services.AddSingleton<IProductImageStorage, FakeProductImageStorage>();
        },
        useInMemoryDatabase: true);

    [Fact]
    public async Task Unauthenticated_user_cannot_upload_product_images()
    {
        using var client = _factory.CreateHttpsClient();
        using var response = await UploadAsync(client);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_role_is_forbidden_from_uploading_product_images()
    {
        using var client = _factory.CreateHttpsClient();
        await _factory.SeedUserAsync("image-user@gymshop.test", "User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.LoginAsync(client, "image-user@gymshop.test"));
        using var response = await UploadAsync(client);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Administrative_roles_can_upload_product_images(string role)
    {
        using var client = _factory.CreateHttpsClient();
        var email = $"image-{role.ToLowerInvariant()}@gymshop.test";
        await _factory.SeedUserAsync(email, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await _factory.LoginAsync(client, email));
        using var response = await UploadAsync(client);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client)
    {
        var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "file", "product.png");
        return client.PostAsync("/api/products/images", content);
    }

    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => ((IAsyncLifetime)_factory).DisposeAsync();

    private sealed class FakeProductImageStorage : IProductImageStorage
    {
        public Task<ProductImageUpload> UploadAsync(Stream content, string contentType, int? productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProductImageUpload("https://storage.test/gymshop-product-images/products/draft/test.jpg", "products/draft/test.jpg"));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryGetManagedKey(string? url, out string key) { key = string.Empty; return false; }
    }
}
