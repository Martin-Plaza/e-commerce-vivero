using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class Payment
{
    public int Id { get; set; }
    public int? OrderId { get; set; }
    public int? CheckoutSessionId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public string? ProviderPreferenceId { get; set; }
    public string? ProviderPaymentId { get; set; }
    public string? IdempotencyKey { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? CheckoutUrl { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PaidAt { get; set; }

    public Order? Order { get; set; }
    public CheckoutSession? CheckoutSession { get; set; }
    public ICollection<BillingDocument> BillingDocuments { get; set; } = new List<BillingDocument>();
}

