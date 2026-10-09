using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Infrastructure;

public sealed class NeonProductImageStorageTests
{
    [Fact]
    public async Task Missing_configuration_is_reported_as_a_storage_failure_on_upload()
    {
        var configuration = new ConfigurationBuilder().Build();
        var storage = new NeonProductImageStorage(
            configuration,
            Options.Create(new ProductImageStorageOptions()),
            NullLogger<NeonProductImageStorage>.Instance);

        var exception = await Assert.ThrowsAsync<ProductImageStorageException>(() =>
            storage.UploadAsync(new MemoryStream([0xff, 0xd8, 0xff]), "image/jpeg", null));

        Assert.DoesNotContain("AWS_SECRET_ACCESS_KEY", exception.Message, StringComparison.Ordinal);
    }
}
