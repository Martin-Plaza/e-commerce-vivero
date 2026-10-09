using GymShop.Application.Abstractions;
using GymShop.Application.DTOs.Payments;
using GymShop.Application.UseCases.Payments;
using GymShop.Domain.Entities;
using GymShop.Domain.Enums;
using GymShop.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace GymShop.Tests.Integration;

[Trait("Category", "Integration")]
[Trait("Category", "Postgres")]
[Trait("Category", "Concurrency")]
public sealed class PostgresPaymentConcurrencyTests
{
    [Fact]
    public async Task Concurrent_requests_with_same_key_create_one_payment_and_call_gateway_once()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        var gateway = new BlockingGateway();

        var (winner, loser) = await RunOverlappingRequestsAsync(database, seed, gateway, "same-key", "same-key");

        Assert.True(winner.IsSuccess);
        Assert.True(loser.IsSuccess);
        Assert.Equal(PaymentStatus.Pending.ToString(), winner.Value!.Status);
        Assert.Equal(PaymentStatus.Creating.ToString(), loser.Value!.Status);
        Assert.Equal(winner.Value.Id, loser.Value.Id);
        Assert.Equal(1, gateway.Calls);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.Payments.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_requests_with_different_keys_reuse_the_single_active_payment()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        var gateway = new BlockingGateway();

        var (winner, loser) = await RunOverlappingRequestsAsync(database, seed, gateway, "winner-key", "loser-key");

        Assert.True(winner.IsSuccess);
        Assert.True(loser.IsSuccess);
        Assert.Equal(winner.Value!.Id, loser.Value!.Id);
        Assert.Equal("winner-key", loser.Value.IdempotencyKey);
        Assert.Equal(1, gateway.Calls);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.Payments.Where(x =>
            x.Status == PaymentStatus.Creating || x.Status == PaymentStatus.Pending).ToListAsync());
    }

    [Fact]
    public async Task Filtered_unique_index_rejects_two_active_payments_but_allows_history()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using var db = database.CreateContext();
        db.Payments.Add(CreatePayment(seed.OrderId, "first", PaymentStatus.Pending));
        await db.SaveChangesAsync();

        db.Payments.Add(CreatePayment(seed.OrderId, "second", PaymentStatus.Creating));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Payments.Add(CreatePayment(seed.OrderId, "historical", PaymentStatus.CreationFailed));
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Payments.CountAsync());
    }

    [Fact]
    public async Task Simultaneous_reservation_insert_loser_handles_unique_violation_and_reuses_winner()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        var barrier = new ReservationSaveBarrier(expectedArrivals: 2);
        var gateway = new ImmediateGateway();
        await using var firstContext = database.CreateContext(barrier);
        await using var secondContext = database.CreateContext(barrier);

        var firstTask = new CreatePaymentUseCase(firstContext, [gateway]).ExecuteAsync(
            seed.OrderId, seed.UserId, false, new CreatePaymentRequest("Mock", "race-key-1"));
        var secondTask = new CreatePaymentUseCase(secondContext, [gateway]).ExecuteAsync(
            seed.OrderId, seed.UserId, false, new CreatePaymentRequest("Mock", "race-key-2"));
        await barrier.AllArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        barrier.Release.TrySetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(results[0].Value!.Id, results[1].Value!.Id);
        Assert.Equal(1, gateway.Calls);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.Payments.ToListAsync());
    }

    [Fact]
    public async Task Gateway_failure_creates_history_and_new_key_can_retry()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using (var failedContext = database.CreateContext())
        {
            var failed = await new CreatePaymentUseCase(failedContext, [new FailingGateway()])
                .ExecuteAsync(seed.OrderId, seed.UserId, false, new CreatePaymentRequest("Mock", "failed-key"));
            Assert.False(failed.IsSuccess);
        }

        await using (var retryContext = database.CreateContext())
        {
            var retry = await new CreatePaymentUseCase(retryContext, [new ImmediateGateway()])
                .ExecuteAsync(seed.OrderId, seed.UserId, false, new CreatePaymentRequest("Mock", "retry-key"));
            Assert.True(retry.IsSuccess);
            Assert.Equal(PaymentStatus.Pending.ToString(), retry.Value!.Status);
        }

        await using var verification = database.CreateContext();
        var attempts = await verification.Payments.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(PaymentStatus.CreationFailed, attempts[0].Status);
        Assert.Equal("Gateway de prueba no disponible.", attempts[0].FailureReason);
        Assert.Equal(PaymentStatus.Pending, attempts[1].Status);
    }

    [Fact]
    public async Task Stale_creating_payment_is_recovered_with_its_original_key()
    {
        await using var database = await SqlTestDatabase.CreateMigratedAsync();
        var seed = await database.SeedPendingOrderAsync();
        await using (var seedContext = database.CreateContext())
        {
            var stale = CreatePayment(seed.OrderId, "original-key", PaymentStatus.Creating);
            stale.CreatedAt = DateTime.UtcNow.AddMinutes(-10);
            stale.UpdatedAt = stale.CreatedAt;
            seedContext.Payments.Add(stale);
            await seedContext.SaveChangesAsync();
        }

        var gateway = new ImmediateGateway();
        await using var recoveryContext = database.CreateContext();
        var result = await new CreatePaymentUseCase(
                recoveryContext,
                [gateway],
                PaymentCreationPolicy.FromSeconds(300))
            .ExecuteAsync(seed.OrderId, seed.UserId, false, new CreatePaymentRequest("Mock", "new-request-key"));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Pending.ToString(), result.Value!.Status);
        Assert.Equal("original-key", result.Value.IdempotencyKey);
        Assert.Equal("original-key", gateway.LastIdempotencyKey);
        Assert.Equal(1, gateway.Calls);
        await using var verification = database.CreateContext();
        Assert.Single(await verification.Payments.ToListAsync());
    }

    private static async Task<(GymShop.Application.Common.AppResult<PaymentResponse> Winner, GymShop.Application.Common.AppResult<PaymentResponse> Loser)>
        RunOverlappingRequestsAsync(
            SqlTestDatabase database,
            SeedResult seed,
            BlockingGateway gateway,
            string winnerKey,
            string loserKey)
    {
        await using var winnerContext = database.CreateContext();
        await using var loserContext = database.CreateContext();
        var winnerTask = new CreatePaymentUseCase(winnerContext, [gateway]).ExecuteAsync(
            seed.OrderId,
            seed.UserId,
            false,
            new CreatePaymentRequest("Mock", winnerKey));
        await gateway.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var loser = await new CreatePaymentUseCase(loserContext, [gateway]).ExecuteAsync(
            seed.OrderId,
            seed.UserId,
            false,
            new CreatePaymentRequest("Mock", loserKey));
        gateway.Release.TrySetResult();
        var winner = await winnerTask;
        return (winner, loser);
    }

    private static Payment CreatePayment(int orderId, string key, PaymentStatus status) => new()
    {
        OrderId = orderId,
        Provider = "Mock",
        ExternalReference = $"order-{orderId}",
        IdempotencyKey = key,
        Amount = 100,
        Currency = "ARS",
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    private sealed class BlockingGateway : IPaymentGateway
    {
        private int _calls;
        public int Calls => _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CanHandle(string provider) => provider == "Mock";

        public async Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new PaymentPreferenceResult("Mock", $"pref-{order.Id}", $"mock://checkout/{order.Id}");
        }

        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ImmediateGateway : IPaymentGateway
    {
        public int Calls { get; private set; }
        public string? LastIdempotencyKey { get; private set; }
        public bool CanHandle(string provider) => provider == "Mock";

        public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastIdempotencyKey = idempotencyKey;
            return Task.FromResult(new PaymentPreferenceResult("Mock", $"pref-{order.Id}", $"mock://checkout/{order.Id}"));
        }

        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingGateway : IPaymentGateway
    {
        public bool CanHandle(string provider) => provider == "Mock";
        public Task<PaymentPreferenceResult> CreatePreferenceAsync(Order order, string? idempotencyKey, string? externalReference = null, CancellationToken cancellationToken = default) =>
            throw new PaymentGatewayException("Gateway de prueba no disponible.");
        public Task<ProviderPaymentResult> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ReservationSaveBarrier : SaveChangesInterceptor
    {
        private readonly int _expectedArrivals;
        private int _arrivals;

        public ReservationSaveBarrier(int expectedArrivals)
        {
            _expectedArrivals = expectedArrivals;
        }

        public TaskCompletionSource AllArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var isReservation = eventData.Context?.ChangeTracker.Entries<Payment>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.Status == PaymentStatus.Creating) == true;
            if (isReservation && Interlocked.Increment(ref _arrivals) == _expectedArrivals)
            {
                AllArrived.TrySetResult();
            }

            if (isReservation)
            {
                await Release.Task.WaitAsync(cancellationToken);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

internal sealed class SqlTestDatabase : IAsyncDisposable
{
    private readonly string _connectionString;

    private SqlTestDatabase(string connectionString)
    {
        _connectionString = connectionString;
    }

    public static Task<SqlTestDatabase> CreateMigratedAsync() => CreateAsync();

    public GymShopDbContext CreateContext(params IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<GymShopDbContext>()
            .UseNpgsql(_connectionString);
        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        var options = builder.Options;
        return new GymShopDbContext(options);
    }

    public async Task<SeedResult> SeedPendingOrderAsync()
    {
        await using var db = CreateContext();
        var role = await db.Roles.SingleAsync(x => x.Name == "User");
        var user = new User
        {
            Email = $"postgres-{Guid.NewGuid():N}@test.com",
            Name = "Postgres Test",
            PasswordHash = "not-used",
            RoleId = role.Id,
            IsActive = true
        };
        var product = new Product
        {
            Name = $"Postgres Product {Guid.NewGuid():N}",
            Price = 100,
            Stock = 4,
            IsActive = true
        };
        db.AddRange(user, product);
        await db.SaveChangesAsync();
        var order = new Order
        {
            UserId = user.Id,
            User = user,
            Status = OrderStatus.Pending,
            Total = 100,
            ShippingAddress = "Postgres Test Address"
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            Product = product,
            ProductName = product.Name,
            UnitPrice = 100,
            Quantity = 1,
            Subtotal = 100
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return new SeedResult(user.Id, order.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static async Task<SqlTestDatabase> CreateAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable("GYMSHOP_TEST_POSTGRES") ??
            "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        var builder = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = $"gymshop_tests_{Guid.NewGuid():N}"
        };
        var database = new SqlTestDatabase(builder.ConnectionString);
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        return database;
    }
}

internal sealed record SeedResult(int UserId, int OrderId);
