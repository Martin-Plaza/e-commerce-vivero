using GymShop.Domain.Entities;

namespace GymShop.Application.Abstractions;

public record PaymentPreferenceResult(
    string Provider,
    string ProviderPreferenceId,
    string CheckoutUrl
);

public record ProviderPaymentResult(
    string ProviderPaymentId,
    string ExternalReference,
    string Status,
    decimal Amount,
    string Currency,
    string? FailureReason
);

public interface IPaymentGateway
{
    bool CanHandle(string provider);
    Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default);
    Task<PaymentPreferenceResult> CreatePreferenceAsync(CheckoutSession checkout, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
    {
        var projection = new Order
        {
            Id = checkout.Id,
            Total = checkout.Total,
            ShippingCost = checkout.ShippingCost
        };
        foreach (var item in checkout.Items)
        {
            projection.Items.Add(new OrderItem
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                Subtotal = item.Subtotal
            });
        }
        return CreatePreferenceAsync(projection, idempotencyKey, externalReference, cancellationToken);
    }
    Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default);
    Task<bool> RefundPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task ExpirePreferenceAsync(string providerPreferenceId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }
}
