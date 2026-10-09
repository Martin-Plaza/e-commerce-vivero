using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Services;

public sealed class NeonProductImageStorage : IProductImageStorage
{
    private readonly IAmazonS3? _s3;
    private readonly string _bucket;
    private readonly Uri? _endpoint;
    private readonly ILogger<NeonProductImageStorage> _logger;
    private readonly string[] _missingConfiguration;

    public NeonProductImageStorage(IConfiguration configuration, IOptions<ProductImageStorageOptions> options, ILogger<NeonProductImageStorage> logger)
    {
        _logger = logger;
        _bucket = options.Value.BucketName;
        var endpoint = configuration["AWS_ENDPOINT_URL_S3"];
        var region = configuration["AWS_REGION"];
        var accessKey = configuration["AWS_ACCESS_KEY_ID"];
        var secretKey = configuration["AWS_SECRET_ACCESS_KEY"];
        _missingConfiguration = new[]
        {
            (Name: "PRODUCT_IMAGE_BUCKET", Missing: string.IsNullOrWhiteSpace(_bucket)),
            (Name: "AWS_ENDPOINT_URL_S3", Missing: string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint)),
            (Name: "AWS_REGION", Missing: string.IsNullOrWhiteSpace(region)),
            (Name: "AWS_ACCESS_KEY_ID", Missing: string.IsNullOrWhiteSpace(accessKey)),
            (Name: "AWS_SECRET_ACCESS_KEY", Missing: string.IsNullOrWhiteSpace(secretKey))
        }.Where(item => item.Missing).Select(item => item.Name).ToArray();

        if (_missingConfiguration.Length > 0)
        {
            _logger.LogError(
                "Product image storage is unavailable because required configuration is missing or invalid: {ConfigurationNames}.",
                string.Join(", ", _missingConfiguration));
            return;
        }

        _s3 = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), new AmazonS3Config
        {
            ServiceURL = _endpoint!.ToString().TrimEnd('/'),
            AuthenticationRegion = region,
            ForcePathStyle = true
        });
    }

    public async Task<ProductImageUpload> UploadAsync(Stream content, string contentType, int? productId, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        var extension = contentType switch { "image/jpeg" => "jpg", "image/png" => "png", "image/webp" => "webp", _ => throw new ArgumentOutOfRangeException(nameof(contentType)) };
        var key = $"products/{(productId is > 0 ? productId.Value.ToString() : "draft")}/{Guid.NewGuid():N}.{extension}";
        try
        {
            await _s3!.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket, Key = key, InputStream = content, ContentType = contentType,
                Headers = { CacheControl = "public,max-age=31536000,immutable" }
            }, cancellationToken);
            return new ProductImageUpload(BuildUrl(key), key);
        }
        catch (AmazonS3Exception exception)
        {
            _logger.LogError(exception, "Neon Object Storage rejected a product image upload.");
            throw new ProductImageStorageException("No se pudo almacenar la imagen.", exception);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsSafeKey(key)) return;
        EnsureAvailable();
        try { await _s3!.DeleteObjectAsync(_bucket, key, cancellationToken); }
        catch (AmazonS3Exception exception)
        {
            _logger.LogError(exception, "Neon Object Storage rejected deletion of product image {Key}.", key);
            throw new ProductImageStorageException("No se pudo eliminar la imagen.", exception);
        }
    }

    public bool TryGetManagedKey(string? url, out string key)
    {
        key = string.Empty;
        if (_endpoint is null || string.IsNullOrWhiteSpace(_bucket)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var candidate)) return false;
        var prefix = $"/{Uri.EscapeDataString(_bucket)}/";
        if (!candidate.Scheme.Equals(_endpoint.Scheme, StringComparison.OrdinalIgnoreCase)
            || !candidate.Host.Equals(_endpoint.Host, StringComparison.OrdinalIgnoreCase)
            || !candidate.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)) return false;
        key = Uri.UnescapeDataString(candidate.AbsolutePath[prefix.Length..]);
        return IsSafeKey(key);
    }

    private void EnsureAvailable()
    {
        if (_missingConfiguration.Length > 0 || _s3 is null || _endpoint is null)
            throw new ProductImageStorageException("El almacenamiento de imágenes no está configurado en este entorno.");
    }

    private string BuildUrl(string key) => $"{_endpoint!.ToString().TrimEnd('/')}/{Uri.EscapeDataString(_bucket)}/{string.Join('/', key.Split('/').Select(Uri.EscapeDataString))}";
    private static bool IsSafeKey(string key) => key.StartsWith("products/", StringComparison.Ordinal) && !key.Contains("..", StringComparison.Ordinal) && !key.StartsWith('/') && key.Length <= 1024;
}
