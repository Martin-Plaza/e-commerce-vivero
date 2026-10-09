using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;

namespace GymShop.Infrastructure.Services;

public sealed class BankTransferPaymentGateway : IPaymentGateway
{
    public bool CanHandle(string provider) => string.Equals(provider, "BankTransfer", StringComparison.OrdinalIgnoreCase);

    public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentPreferenceResult("BankTransfer", $"transfer-{externalReference ?? order.Id.ToString()}", string.Empty));

    public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) =>
        throw new PaymentGatewayException("Las transferencias se confirman manualmente luego de verificar la acreditacion.");
}
