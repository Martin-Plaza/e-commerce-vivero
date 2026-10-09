namespace GymShop.Domain.Enums;

public enum PaymentStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Canceled = 4,
    Expired = 5,
    Refunded = 6,
    Creating = 7,
    CreationFailed = 8
}
