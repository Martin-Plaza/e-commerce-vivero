using System.Buffers.Binary;
using System.Security.Cryptography;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace GymShop.Tests.Services;

public sealed class MfaServiceTests
{
    private const string EncryptionKey = "R3ltU2hvcC1Mb2NhbC1NZmEtS2V5LTMyLUJ5dGVzISE=";
    private const string JwtSecret = "test-jwt-secret-that-is-long-enough-for-hs256";

    [Fact]
    public async Task Privileged_user_enrolls_totp_and_can_use_one_recovery_code_once()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "Admin");
        var user = new User { Email = "admin@test.com", Name = "Admin", PasswordHash = "unused", Role = role, RoleId = role.Id, IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var challenge = await service.CreateLoginRequirementAsync(user.Id);
        Assert.NotNull(challenge);
        Assert.True(challenge.SetupRequired);

        var setup = await service.BeginSetupAsync(challenge.ChallengeToken);
        Assert.NotNull(setup);
        Assert.NotEmpty(setup.QrCodeRows);
        var enabled = await service.EnableAsync(challenge.ChallengeToken, CurrentTotp(setup.SharedKey));

        Assert.NotNull(enabled);
        Assert.Equal(8, enabled.RecoveryCodes.Count);
        Assert.NotNull(user.MfaEnabledAtUtc);
        var login = await service.CreateLoginRequirementAsync(user.Id);
        Assert.NotNull(login);
        Assert.False(login.SetupRequired);

        var recoveryCode = enabled.RecoveryCodes[0];
        Assert.NotNull(await service.CompleteAsync(login.ChallengeToken, recoveryCode));
        Assert.Null(await service.CompleteAsync(login.ChallengeToken, recoveryCode));
    }

    [Fact]
    public async Task Customer_does_not_require_mfa()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = "user@test.com", Name = "User", PasswordHash = "unused", Role = role, RoleId = role.Id, IsActive = true };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        Assert.Null(await CreateService(db).CreateLoginRequirementAsync(user.Id));
    }

    private static MfaService CreateService(GymShop.Infrastructure.Data.GymShopDbContext db) => new(
        db,
        Options.Create(new MfaOptions { Issuer = "GymShop Test", EncryptionKey = EncryptionKey, ChallengeMinutes = 5 }),
        Options.Create(new JwtOptions { Issuer = "GymShop.Tests", Audience = "GymShop.Client", Secret = JwtSecret, ExpirationMinutes = 15 }),
        TimeProvider.System);

    private static string CurrentTotp(string base32)
    {
        var secret = DecodeBase32(base32);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return output.ToArray();
    }
}
