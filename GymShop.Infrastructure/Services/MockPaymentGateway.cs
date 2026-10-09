using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;

namespace GymShop.Infrastructure.Services;

public class MockPaymentGateway : IPaymentGateway
{
    public bool CanHandle(string provider) => string.Equals(provider, "Mock", StringComparison.OrdinalIgnoreCase);

    public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PaymentPreferenceResult(
            "Mock",
            $"mock-pref-{order.Id}",
            $"mock://checkout/orders/{order.Id}"
        ));
    }

    public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        throw new PaymentGatewayException("El gateway Mock no consulta pagos externos.");
    }
}
