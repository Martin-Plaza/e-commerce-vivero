using System.ComponentModel.DataAnnotations;
using System.Reflection;
using GymShop.Application.DTOs.Auth;
using GymShop.Application.DTOs.Carts;
using GymShop.Application.DTOs.Orders;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.DTOs.Products;

namespace GymShop.Tests.Api;

public class RequestValidationTests
{
    [Fact]
    public void Invalid_email_is_rejected_by_http_contract()
    {
        AssertInvalid(new RegisterRequest("Cliente", "Apellido", "email-invalido", "clave123"), nameof(RegisterRequest.Email));
    }

    [Theory]
    [MemberData(nameof(ValidCheckoutAmounts))]
    public void Checkout_accepts_non_negative_shipping_and_discount(CheckoutCartRequest request) =>
        Assert.Empty(Validate(request));

    [Theory]
    [MemberData(nameof(InvalidCheckoutAmounts))]
    public void Checkout_rejects_invalid_amounts(CheckoutCartRequest request, string member) =>
        AssertInvalid(request, member);

    [Theory]
    [MemberData(nameof(ValuesOverDatabaseLimits))]
    public void Values_over_database_limits_are_rejected_before_controller_execution(object request, string member)
    {
        AssertInvalid(request, member);
    }

    [Theory]
    [InlineData("/images/productos/mancuerna.jpg")]
    [InlineData("https://cdn.example.com/mancuerna.jpg")]
    public void Supported_image_locations_are_valid(string imageUrl)
    {
        Assert.Empty(Validate(new CreateProductRequest("Mancuerna", null, 10, 1, imageUrl)));
    }

    [Theory]
    [InlineData("C:\\privado\\imagen.jpg")]
    [InlineData("../privado/imagen.jpg")]
    [InlineData("file:///privado/imagen.jpg")]
    [InlineData("//otro-host/imagen.jpg")]
    public void Physical_or_unsafe_image_locations_are_rejected(string imageUrl)
    {
        AssertInvalid(new CreateProductRequest("Mancuerna", null, 10, 1, imageUrl), nameof(CreateProductRequest.ImageUrl));
    }

    [Theory]
    [InlineData("corta1")]
    [InlineData("sololetras")]
    [InlineData("12345678")]
    public void Weak_password_is_rejected(string password)
    {
        AssertInvalid(new RegisterRequest("Cliente", "Apellido", "cliente@example.com", password), nameof(RegisterRequest.Password));
        AssertInvalid(new ConfirmPasswordResetRequest("cliente@example.com", "123456", password), nameof(ConfirmPasswordResetRequest.NewPassword));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    public void Password_reset_requires_exactly_six_digits(string code) =>
        AssertInvalid(new ConfirmPasswordResetRequest("cliente@example.com", code, "NuevaClave123"), nameof(ConfirmPasswordResetRequest.Code));

    public static TheoryData<object, string> ValuesOverDatabaseLimits => new()
    {
        { new RegisterRequest(new string('N', 101), "Apellido", "cliente@example.com", "clave123"), nameof(RegisterRequest.Name) },
        { new RegisterRequest("Cliente", "Apellido", new string('a', 245) + "@example.com", "clave123"), nameof(RegisterRequest.Email) },
        { new CreateProductRequest(new string('N', 151), null, 10, 1, null), nameof(CreateProductRequest.Name) },
        { new CreateProductRequest("Producto", new string('D', 1001), 10, 1, null), nameof(CreateProductRequest.Description) },
        { new CreateProductRequest("Producto", null, 10, 1, "/" + new string('i', 500)), nameof(CreateProductRequest.ImageUrl) },
        { new CheckoutCartRequest("HomeDelivery", new string('A', 301), 0), nameof(CheckoutCartRequest.ShippingAddress) },
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 100, 0, new string('K', 101)), nameof(CheckoutCartRequest.IdempotencyKey) },
        { new CreatePaymentRequest("Mock", new string('K', 101)), nameof(CreatePaymentRequest.IdempotencyKey) },
        { new CreatePaymentRequest(new string('P', 51), null), nameof(CreatePaymentRequest.Provider) },
        { new UpdatePaymentStatusRequest("Rejected", null, new string('R', 501)), nameof(UpdatePaymentStatusRequest.FailureReason) },
        { new CancelOrderRequest(new string('R', 501)), nameof(CancelOrderRequest.Reason) }
    };

    public static TheoryData<CheckoutCartRequest> ValidCheckoutAmounts => new()
    {
        // Sin cupón: el descuento cero es un importe válido.
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 100, 500, 0) },
        // Retiro en tienda: no tiene costo de envío.
        { new CheckoutCartRequest("StorePickup", null, 0, 500, 0) },
        // Envío gratis a domicilio: costo cero con dirección obligatoria.
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 500, 0) }
    };

    public static TheoryData<CheckoutCartRequest, string> InvalidCheckoutAmounts => new()
    {
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", -1, 500, 0), nameof(CheckoutCartRequest.ExpectedShippingCost) },
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 0, 0), nameof(CheckoutCartRequest.ExpectedSubtotal) },
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 500, -1), nameof(CheckoutCartRequest.ExpectedDiscount) },
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0.001m, 500, 0), nameof(CheckoutCartRequest.ExpectedShippingCost) },
        { new CheckoutCartRequest("HomeDelivery", "Calle 123", 0, 500, 0.001m), nameof(CheckoutCartRequest.ExpectedDiscount) }
    };

    private static void AssertInvalid(object request, string member)
    {
        Assert.Contains(Validate(request), result =>
            result.MemberNames.Contains(member, StringComparer.OrdinalIgnoreCase));
    }

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        var constructor = request.GetType().GetConstructors().Single();
        foreach (var parameter in constructor.GetParameters())
        {
            var property = request.GetType().GetProperty(parameter.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            var value = property.GetValue(request);
            foreach (var attribute in parameter.GetCustomAttributes<ValidationAttribute>())
            {
                var context = new ValidationContext(request) { MemberName = property.Name };
                var result = attribute.GetValidationResult(value, context);
                if (result != ValidationResult.Success) results.Add(result!);
            }
        }
        return results;
    }
}
