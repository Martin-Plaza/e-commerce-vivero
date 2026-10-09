using System.ComponentModel.DataAnnotations;

namespace GymShop.Application.Common;

public static class ValidationLimits
{
    public const int Email = 256;
    public const int UserName = 100;
    public const int Phone = 30;
    public const int PasswordMin = 8;
    public const int PasswordMax = 128;
    public const int ProductName = 150;
    public const int ProductDescription = 1000;
    public const int ImageUrl = 500;
    public const int ShippingAddress = 300;
    public const int ShippingPostalCode = 16;
    public const int ShippingProvince = 100;
    public const int ShippingCity = 100;
    public const int ShippingStreet = 150;
    public const int ShippingStreetNumber = 20;
    public const int ShippingFloor = 20;
    public const int ShippingApartment = 20;
    public const int ShippingNotes = 300;
    public const int ShippingProviderCode = 50;
    public const int ShippingServiceCode = 100;
    public const int ShippingServiceName = 150;
    public const int PickupHours = 300;
    public const int PickupInstructions = 1000;
    public const int Carrier = 100;
    public const int TrackingNumber = 100;
    public const int TrackingUrl = 500;
    public const int IdempotencyKey = 100;
    public const int PaymentProvider = 50;
    public const int PaymentProviderId = 100;
    public const int PaymentFailureReason = 500;
    public const int CancellationReason = 500;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public StrongPasswordAttribute() =>
        ErrorMessage = "La password debe tener entre 8 y 128 caracteres e incluir al menos una letra y un numero.";

    public override bool IsValid(object? value) =>
        value is string password
        && password.Length is >= ValidationLimits.PasswordMin and <= ValidationLimits.PasswordMax
        && password.Any(char.IsLetter)
        && password.Any(char.IsDigit);
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ProductImageUrlAttribute : ValidationAttribute
{
    public ProductImageUrlAttribute() =>
        ErrorMessage = "ImageUrl debe ser una URL http/https o una ruta web local que comience con '/'.";

    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 }) return true;
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (text.StartsWith('/') && !text.StartsWith("//", StringComparison.Ordinal) && !text.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class SqlDecimalAttribute : ValidationAttribute
{
    internal const decimal Maximum = 9999999999999999.99m;

    public SqlDecimalAttribute() =>
        ErrorMessage = "El precio debe ser mayor a cero, tener hasta 16 digitos enteros y hasta 2 decimales.";

    public override bool IsValid(object? value) =>
        value is null || value is decimal amount && amount > 0 && amount <= Maximum && decimal.Round(amount, 2) == amount;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NonNegativeSqlDecimalAttribute : ValidationAttribute
{
    public NonNegativeSqlDecimalAttribute() =>
        ErrorMessage = "El importe no puede ser negativo, debe tener hasta 16 digitos enteros y hasta 2 decimales.";

    public override bool IsValid(object? value) =>
        value is null || value is decimal amount && amount >= 0 && amount <= SqlDecimalAttribute.Maximum && decimal.Round(amount, 2) == amount;
}
