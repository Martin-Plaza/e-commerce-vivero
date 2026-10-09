using GymShop.Domain.Enums;

namespace GymShop.Domain.Entities;

public class NotificationOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public TransactionalNotificationType Type { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public string DeduplicationKey { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public int? PaymentId { get; set; }
    public Guid? BillingDocumentId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    public string? LastFailureType { get; set; }

    public Order? Order { get; set; }
    public Payment? Payment { get; set; }
    public BillingDocument? BillingDocument { get; set; }
}
