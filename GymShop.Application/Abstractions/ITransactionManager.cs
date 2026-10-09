namespace GymShop.Application.Abstractions;

public interface IApplicationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface ITransactionManager
{
    Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<IApplicationTransaction> BeginUserAdministrationTransactionAsync(CancellationToken cancellationToken = default);
    Task<IApplicationTransaction> BeginCheckoutTransactionAsync(CancellationToken cancellationToken = default);
    Task<IApplicationTransaction> BeginOrderCancellationTransactionAsync(int orderId, CancellationToken cancellationToken = default);
    Task<IApplicationTransaction> BeginCouponCheckoutTransactionAsync(CancellationToken cancellationToken = default);
    Task<IApplicationTransaction> BeginPasswordResetActivationTransactionAsync(int userId, CancellationToken cancellationToken = default);
}
