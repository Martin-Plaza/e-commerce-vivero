using System.Security.Cryptography;
using GymShop.Application.Abstractions;

namespace GymShop.Infrastructure.Services;

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return string.Join('.', Convert.ToBase64String(salt), Convert.ToBase64String(key), Iterations, Algorithm.Name);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (!TryRead(passwordHash, out var salt, out var expectedKey, out var iterations, out var algorithm))
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, algorithm, expectedKey.Length);

        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }

    public bool NeedsRehash(string passwordHash) =>
        !TryRead(passwordHash, out var salt, out var key, out var iterations, out var algorithm) ||
        salt.Length != SaltSize || key.Length != KeySize || iterations < Iterations || algorithm != Algorithm;

    private static bool TryRead(
        string passwordHash,
        out byte[] salt,
        out byte[] key,
        out int iterations,
        out HashAlgorithmName algorithm)
    {
        salt = [];
        key = [];
        iterations = 0;
        algorithm = default;
        var parts = passwordHash.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 ||
            !int.TryParse(parts[2], out iterations) || iterations <= 0 ||
            !string.Equals(parts[3], Algorithm.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            salt = Convert.FromBase64String(parts[0]);
            key = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || key.Length == 0) return false;
        algorithm = Algorithm;
        return true;
    }
}
