using GymShop.Domain.Enums;

namespace GymShop.Tests.Domain;

public class OrderStatusCompatibilityTests
{
    [Fact]
    public void Numeric_values_preserve_persisted_order_status_compatibility()
    {
        Assert.Equal(1, (int)OrderStatus.Pending);
        Assert.Equal(2, (int)OrderStatus.Paid);
        Assert.Equal(3, (int)OrderStatus.Shipped);
        Assert.Equal(4, (int)OrderStatus.Canceled);
        Assert.Equal(5, (int)OrderStatus.Refunded);
        Assert.Equal(6, (int)OrderStatus.Preparing);
        Assert.Equal(7, (int)OrderStatus.Delivered);
    }
}
