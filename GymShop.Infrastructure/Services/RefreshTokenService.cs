using System.Security.Cryptography;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GymShop.Infrastructure.Services;

public sealed class RefreshTokenService(
    IApplicationDbContext db,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenService
{
    public async Task<string> IssueAsync(int userId, CancellationToken cancellationToken = default)
    {
        var token = CreateToken();
        var tokenVersion = await db.Users.Where(x => x.Id == userId).Select(x => x.TokenVersion).SingleAsync(cancellationToken);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            TokenVersion = tokenVersion,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            ExpiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(options.Value.RefreshExpirationDays)
        });
        await db.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var hash = Hash(token);
        var current = await db.RefreshTokens
            .Include(x => x.User)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);

        if (current is null || current.RevokedAtUtc is not null || current.ExpiresAtUtc <= now ||
            !current.User.IsActive || current.TokenVersion != current.User.TokenVersion)
            return null;

        var replacement = CreateToken();
        var replacementHash = Hash(replacement);
        current.RevokedAtUtc = now;
        current.ReplacedByTokenHash = replacementHash;
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = current.UserId,
            TokenHash = replacementHash,
            TokenVersion = current.User.TokenVersion,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(options.Value.RefreshExpirationDays)
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return null;
        }
        return new RefreshTokenRotation(current.User, replacement);
    }

    public async Task RevokeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        var hash = Hash(token);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (current is null || current.RevokedAtUtc is not null) return;
        current.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
