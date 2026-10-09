namespace GymShop.Domain.Enums;

public enum CheckoutStatus
{
    AwaitingPayment = 1,
    Completed = 2,
    PaymentFailed = 3,
    StockUnavailable = 4,
    Refunded = 5
}
