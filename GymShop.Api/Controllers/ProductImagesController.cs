using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;

namespace GymShop.Api.Controllers;

[ApiController]
[Route("api/products/images")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class ProductImagesController : ControllerBase
{
    public const long MaxFileSize = 5 * 1024 * 1024;
    public const int MaxDimension = 8000;
    public const long MaxPixels = 25_000_000;
    private readonly IProductImageStorage _storage;
    private readonly IApplicationDbContext _db;
    private readonly IAuditContext _auditContext;

    public ProductImagesController(IProductImageStorage storage, IApplicationDbContext db, IAuditContext auditContext)
    {
        _storage = storage; _db = db; _auditContext = auditContext;
    }

    [HttpPost]
    [RequestSizeLimit(MaxFileSize + 1024 * 1024)]
    [ProducesResponseType(typeof(ProductImageUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ProductImageUploadResponse>> Upload([FromForm] IFormFile? file, [FromForm] int? productId, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Seleccioná una imagen." });
        if (file.Length > MaxFileSize) return StatusCode(StatusCodes.Status413PayloadTooLarge, new { message = "La imagen no puede superar los 5 MB." });

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream((int)file.Length);
        await input.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        SanitizedImage sanitized;
        try { sanitized = Sanitize(bytes); }
        catch (InvalidImageException)
        {
            return BadRequest(new { message = $"Solo se permiten imágenes JPEG, PNG o WebP completas de hasta {MaxDimension}px y {MaxPixels:N0} píxeles." });
        }

        ProductImageUpload? uploaded = null;
        try
        {
            await using var sanitizedStream = new MemoryStream(sanitized.Bytes, writable: false);
            uploaded = await _storage.UploadAsync(sanitizedStream, sanitized.ContentType, productId, cancellationToken);
            AuditTrail.Add(_db, _auditContext, "ProductImageUploaded", "ProductImage", uploaded.Key, null,
                new { uploaded.Key, contentType = sanitized.ContentType, originalSize = file.Length, sanitizedSize = sanitized.Bytes.Length, sanitized.Width, sanitized.Height, productId });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (ProductImageStorageException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "El servicio de imágenes no está disponible. Intentá nuevamente." });
        }
        catch
        {
            if (uploaded is not null)
            {
                try { await _storage.DeleteAsync(uploaded.Key, CancellationToken.None); } catch (ProductImageStorageException) { }
            }
            throw;
        }

        return Created(uploaded.Url, new ProductImageUploadResponse(uploaded.Url, uploaded.Key));
    }

    [HttpDelete]
    public async Task<ActionResult> Delete(ProductImageDeleteRequest request, CancellationToken cancellationToken)
    {
        var key = request.Key?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(key) && !_storage.TryGetManagedKey(request.Url, out key)) return NoContent();
        try { await _storage.DeleteAsync(key, cancellationToken); }
        catch (ProductImageStorageException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "El servicio de imágenes no está disponible. Intentá nuevamente." });
        }
        return NoContent();
    }

    public static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a })) return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public static SanitizedImage Sanitize(byte[] bytes)
    {
        try
        {
            var info = Image.Identify(bytes) ?? throw new InvalidImageException();
            var contentType = info.Metadata.DecodedImageFormat?.DefaultMimeType;
            if (contentType is not ("image/jpeg" or "image/png" or "image/webp") ||
                info.Width <= 0 || info.Height <= 0 || info.Width > MaxDimension || info.Height > MaxDimension ||
                (long)info.Width * info.Height > MaxPixels)
                throw new InvalidImageException();

            using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, bytes);
            image.Mutate(context => context.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            using var output = new MemoryStream();
            switch (contentType)
            {
                case "image/jpeg": image.Save(output, new JpegEncoder { Quality = 90 }); break;
                case "image/png": image.Save(output, new PngEncoder()); break;
                case "image/webp": image.Save(output, new WebpEncoder { Quality = 90 }); break;
            }
            if (output.Length == 0 || output.Length > MaxFileSize) throw new InvalidImageException();
            return new SanitizedImage(output.ToArray(), contentType, image.Width, image.Height);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException or ArgumentException)
        {
            throw new InvalidImageException();
        }
    }
}

public sealed record ProductImageUploadResponse(string Url, string Key);
public sealed record ProductImageDeleteRequest(string? Key, string? Url);
public sealed record SanitizedImage(byte[] Bytes, string ContentType, int Width, int Height);
public sealed class InvalidImageException : Exception;
