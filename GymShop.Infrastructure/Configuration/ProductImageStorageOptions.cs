namespace GymShop.Infrastructure.Configuration;

public sealed class ProductImageStorageOptions
{
    public const string SectionName = "ProductImageStorage";
    public string BucketName { get; set; } = string.Empty;
}
