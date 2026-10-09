using GymShop.Application.Abstractions;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GymShop.Infrastructure.Data;

public sealed class EfTransactionManager : ITransactionManager
{
    private readonly GymShopDbContext _db;

    public EfTransactionManager(GymShopDbContext db)
    {
        _db = db;
    }

    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return new EfApplicationTransaction(transaction);
    }

    public async Task<IApplicationTransaction> BeginUserAdministrationTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // One database-wide transactional mutex makes the count-and-update invariant atomic
            // across every API instance. PostgreSQL releases it automatically on commit/rollback.
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(87324519)", cancellationToken);
            return new EfApplicationTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IApplicationTransaction> BeginCouponCheckoutTransactionAsync(CancellationToken cancellationToken = default)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(87324520)", cancellationToken);
            return new EfApplicationTransaction(transaction);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public async Task<IApplicationTransaction> BeginCheckoutTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            return new NoopApplicationTransaction();

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Acquire the checkout mutex before reading the cart. Besides protecting coupon
            // usage limits, this makes a repeated/concurrent checkout observe the cart emptied
            // by the first request instead of creating a second order from stale data.
            await _db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(87324520)", cancellationToken);
            return new EfApplicationTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IApplicationTransaction> BeginOrderCancellationTransactionAsync(int orderId, CancellationToken cancellationToken = default)
    {
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            return new NoopApplicationTransaction();

        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize repeated cancellation requests for the same order before reading it.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(87324523, {orderId})", cancellationToken);
            return new EfApplicationTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IApplicationTransaction> BeginPasswordResetActivationTransactionAsync(int userId, CancellationToken cancellationToken = default)
    {
        // The test host replaces Npgsql with EF's in-memory provider while retaining
        // relational services in its service collection, so IsRelational() is not a
        // reliable capability check there.
        if (_db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            return new NoopApplicationTransaction();
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize activation only for the affected account across all API instances.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(87324521, {userId})", cancellationToken);
            return new EfApplicationTransaction(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class NoopApplicationTransaction : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EfApplicationTransaction : IApplicationTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfApplicationTransaction(IDbContextTransaction transaction)
        {
            _transaction = transaction;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return CommitCoreAsync(cancellationToken);
        }

        private async Task CommitCoreAsync(CancellationToken cancellationToken)
        {
            try { await _transaction.CommitAsync(cancellationToken); }
            catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
            {
                throw new DbUpdateConcurrencyException("La operación concurrente debe reintentarse.", exception);
            }
        }

        public ValueTask DisposeAsync()
        {
            return _transaction.DisposeAsync();
        }
    }
}
