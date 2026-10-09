using System.Reflection;
using GymShop.Api.Controllers;
using GymShop.Application.Abstractions;
using GymShop.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GymShop.Tests.Api;

public sealed class ProductImageUploadTests
{
    [Fact]
    public void Endpoint_is_restricted_to_admins()
    {
        var authorize = typeof(ProductImagesController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal("Admin,SuperAdmin", authorize?.Roles);
    }

    [Theory]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff, 0x00 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void Detects_allowed_content_from_magic_bytes(byte[] content, string expected) =>
        Assert.Equal(expected, ProductImagesController.DetectContentType(content));

    [Fact]
    public void Rejects_disallowed_content() =>
        Assert.Null(ProductImagesController.DetectContentType("not an image"u8));

    [Fact]
    public async Task Uploads_a_valid_file_and_records_audit()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var storage = new FakeStorage();
        var controller = new ProductImagesController(storage, db, new FakeAuditContext(1, "upload-test"));
        var result = await controller.Upload(FormFile(ValidPng), 42, default);
        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<ProductImageUploadResponse>(created.Value);
        Assert.Equal("products/draft/id.jpg", response.Key);
        Assert.Equal("image/png", storage.LastContentType);
        Assert.Single(db.AuditEntries, entry => entry.Action == "ProductImageUploaded");
    }

    [Fact]
    public async Task Rejects_files_larger_than_five_megabytes()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var controller = new ProductImagesController(new FakeStorage(), db, new FakeAuditContext(1, "upload-test"));
        var file = FormFile(new byte[ProductImagesController.MaxFileSize + 1]);
        var result = await controller.Upload(file, null, default);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task Returns_safe_gateway_error_when_storage_fails()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var controller = new ProductImagesController(new FakeStorage { FailUpload = true }, db, new FakeAuditContext(1, "upload-test"));
        var result = await controller.Upload(FormFile(ValidPng), null, default);
        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, response.StatusCode);
        Assert.DoesNotContain("credential", response.Value?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_truncated_file_even_when_magic_bytes_match()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var controller = new ProductImagesController(new FakeStorage(), db, new FakeAuditContext(1, "upload-test"));
        var result = await controller.Upload(FormFile([0xff, 0xd8, 0xff, 0x00]), null, default);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    private static byte[] ValidPng => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static FormFile FormFile(byte[] content) => new(new MemoryStream(content), 0, content.Length, "file", "original.jpg")
    {
        Headers = new HeaderDictionary(), ContentType = "image/jpeg"
    };

    private sealed class FakeStorage : IProductImageStorage
    {
        public bool FailUpload { get; init; }
        public string? LastContentType { get; private set; }
        public Task<ProductImageUpload> UploadAsync(Stream content, string contentType, int? productId, CancellationToken cancellationToken = default)
        {
            LastContentType = contentType;
            return FailUpload ? throw new ProductImageStorageException("credential secret detail") : Task.FromResult(new ProductImageUpload("https://storage.test/bucket/products/draft/id.jpg", "products/draft/id.jpg"));
        }
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool TryGetManagedKey(string? url, out string key) { key = string.Empty; return false; }
    }
}
