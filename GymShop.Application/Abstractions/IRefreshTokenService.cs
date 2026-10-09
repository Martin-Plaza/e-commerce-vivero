using GymShop.Domain.Entities;

namespace GymShop.Application.Abstractions;

public sealed record RefreshTokenRotation(User User, string RefreshToken);

public interface IRefreshTokenService
{
    Task<string> IssueAsync(int userId, CancellationToken cancellationToken = default);
    Task<RefreshTokenRotation?> RotateAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAsync(string? token, CancellationToken cancellationToken = default);
}
