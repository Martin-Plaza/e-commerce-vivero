namespace GymShop.Infrastructure.Services;

public sealed class ArcaAccessTicketCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ArcaAccessTicket? _ticket;

    public async Task<ArcaAccessTicket> GetOrCreateAsync(
        Func<CancellationToken, Task<ArcaAccessTicket>> factory,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref _ticket);
        if (IsUsable(current, timeProvider)) return current!;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = _ticket;
            if (IsUsable(current, timeProvider)) return current!;
            current = await factory(cancellationToken);
            Volatile.Write(ref _ticket, current);
            return current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsUsable(ArcaAccessTicket? ticket, TimeProvider timeProvider) =>
        ticket is not null && ticket.ExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(2);
}

public sealed record ArcaAccessTicket(string Token, string Sign, DateTimeOffset ExpiresAtUtc);
