using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;

namespace GymShop.Tests.TestSupport;

internal sealed class FakeJwtTokenService : IJwtTokenService
{
    public string CreateToken(User user)
    {
        return $"test-token-for-{user.Id}";
    }
}
