namespace GymShop.Application.Abstractions;

public sealed record ProductImageUpload(string Url, string Key);

public interface IProductImageStorage
{
    Task<ProductImageUpload> UploadAsync(Stream content, string contentType, int? productId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
    bool TryGetManagedKey(string? url, out string key);
}

public sealed class ProductImageStorageException : Exception
{
    public ProductImageStorageException(string message, Exception? innerException = null) : base(message, innerException) { }
}
