namespace GymShop.Domain.Enums;

public enum TransactionalNotificationType
{
    OrderCreated,
    PaymentApproved,
    PaymentRejected,
    OrderPreparing,
    OrderShipped,
    OrderReadyForPickup,
    PaymentRefunded,
    BillingDocumentAvailable,
    OrderExpired,
    StockUnavailableAfterPayment
}

public enum NotificationDeliveryStatus
{
    Pending,
    Processing,
    Sent,
    DeadLetter
}
