using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Services;

public sealed class RefreshTokenServiceTests
{
    [Fact]
    public async Task Rotation_is_single_use_and_replacement_can_rotate()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User
        {
            Email = "refresh@test.com", Name = "Refresh", PasswordHash = "unused",
            Role = role, RoleId = role.Id, IsActive = true, EmailVerifiedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new RefreshTokenService(db, Options.Create(new JwtOptions { RefreshExpirationDays = 14 }), TimeProvider.System);

        var original = await service.IssueAsync(user.Id);
        var firstRotation = await service.RotateAsync(original);
        var replay = await service.RotateAsync(original);
        var secondRotation = await service.RotateAsync(firstRotation!.RefreshToken);

        Assert.NotNull(firstRotation);
        Assert.Null(replay);
        Assert.NotNull(secondRotation);
        Assert.NotEqual(original, firstRotation.RefreshToken);
    }

    [Fact]
    public async Task Inactive_user_cannot_refresh()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = "inactive-refresh@test.com", Name = "Inactive", PasswordHash = "unused", Role = role, RoleId = role.Id, IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new RefreshTokenService(db, Options.Create(new JwtOptions()), TimeProvider.System);
        var token = await service.IssueAsync(user.Id);
        user.IsActive = false;
        await db.SaveChangesAsync();

        Assert.Null(await service.RotateAsync(token));
    }

    [Fact]
    public async Task Credential_version_change_invalidates_existing_refresh_token()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = "version-refresh@test.com", Name = "Version", PasswordHash = "unused", Role = role, RoleId = role.Id, IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new RefreshTokenService(db, Options.Create(new JwtOptions()), TimeProvider.System);
        var token = await service.IssueAsync(user.Id);
        user.TokenVersion++;
        await db.SaveChangesAsync();

        Assert.Null(await service.RotateAsync(token));
    }
}
