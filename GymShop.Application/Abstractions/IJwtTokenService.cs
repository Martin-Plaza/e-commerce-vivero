using GymShop.Domain.Entities;

namespace GymShop.Application.Abstractions;

public interface IJwtTokenService
{
    string CreateToken(User user);
}

public static class JwtClaimNames
{
    public const string TokenVersion = "token_version";
    public const string Mfa = "mfa";
}
