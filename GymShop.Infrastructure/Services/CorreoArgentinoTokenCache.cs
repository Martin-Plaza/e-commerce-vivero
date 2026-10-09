namespace GymShop.Infrastructure.Services;

public sealed class CorreoArgentinoTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string?> GetAsync(
        TimeProvider timeProvider,
        Func<CancellationToken, Task<(string Token, DateTimeOffset ExpiresAt)?>> factory,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(_token) && _expiresAt > now.AddMinutes(1)) return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(_token) && _expiresAt > now.AddMinutes(1)) return _token;

            var created = await factory(cancellationToken);
            if (created is null) return null;
            _token = created.Value.Token;
            _expiresAt = created.Value.ExpiresAt;
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate(string token)
    {
        if (!string.Equals(_token, token, StringComparison.Ordinal)) return;
        _token = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
