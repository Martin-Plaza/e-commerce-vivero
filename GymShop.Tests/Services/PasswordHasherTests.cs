using System.Security.Cryptography;
using GymShop.Infrastructure.Services;

namespace GymShop.Tests.Services;

public sealed class PasswordHasherTests
{
    [Fact]
    public void New_hash_uses_recommended_pbkdf2_cost()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("Clave segura 123");

        Assert.True(hasher.Verify("Clave segura 123", hash));
        Assert.False(hasher.NeedsRehash(hash));
        Assert.Equal("600000", hash.Split('.')[2]);
    }

    [Fact]
    public void Legacy_hash_is_valid_but_requires_rehash()
    {
        var hasher = new PasswordHasher();
        var legacy = LegacyHash("clave123");

        Assert.True(hasher.Verify("clave123", legacy));
        Assert.True(hasher.NeedsRehash(legacy));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("not-base64.not-base64.100000.SHA256")]
    [InlineData("YQ==.Yg==.0.SHA256")]
    [InlineData("YQ==.Yg==.100000.SHA1")]
    public void Malformed_or_unsupported_hash_is_rejected_without_throwing(string storedHash)
    {
        var hasher = new PasswordHasher();

        Assert.False(hasher.Verify("clave123", storedHash));
        Assert.True(hasher.NeedsRehash(storedHash));
    }

    internal static string LegacyHash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return string.Join('.', Convert.ToBase64String(salt), Convert.ToBase64String(key), 100_000, HashAlgorithmName.SHA256.Name);
    }
}
