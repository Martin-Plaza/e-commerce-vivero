using System.Buffers.Binary;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QRCoder;

namespace GymShop.Infrastructure.Services;

public sealed class MfaService(
    IApplicationDbContext db,
    IOptions<MfaOptions> mfaOptions,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : IMfaService
{
    private const string SetupPurpose = "mfa_setup";
    private const string VerifyPurpose = "mfa_verify";
    private const int TotpDigits = 6;
    private const int TotpPeriodSeconds = 30;
    private const int RecoveryCodeCount = 8;
    private readonly MfaOptions _mfa = mfaOptions.Value;
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<MfaLoginRequirement?> CreateLoginRequirementAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null || !IsPrivileged(user.Role.Name)) return null;
        var setupRequired = user.MfaEnabledAtUtc is null || string.IsNullOrWhiteSpace(user.MfaSecretEncrypted);
        return new MfaLoginRequirement(user, setupRequired, CreateChallenge(user, setupRequired ? SetupPurpose : VerifyPurpose));
    }

    public async Task<MfaSetupData?> BeginSetupAsync(string challengeToken, CancellationToken cancellationToken = default)
    {
        var user = await ValidateChallengeAsync(challengeToken, SetupPurpose, cancellationToken);
        if (user is null || user.MfaEnabledAtUtc is not null) return null;

        var secret = RandomNumberGenerator.GetBytes(20);
        user.MfaSecretEncrypted = Encrypt(secret);
        user.MfaLastUsedTimeStep = null;
        await db.SaveChangesAsync(cancellationToken);

        var sharedKey = Base32Encode(secret);
        var label = Uri.EscapeDataString($"{_mfa.Issuer}:{user.Email}");
        var issuer = Uri.EscapeDataString(_mfa.Issuer);
        var uri = $"otpauth://totp/{label}?secret={sharedKey}&issuer={issuer}&algorithm=SHA1&digits={TotpDigits}&period={TotpPeriodSeconds}";
        using var qr = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.M);
        var rows = qr.ModuleMatrix.Select(row => string.Concat(row.Cast<bool>().Select(bit => bit ? '1' : '0'))).ToArray();
        return new MfaSetupData(sharedKey, uri, rows);
    }

    public async Task<MfaVerification?> EnableAsync(string challengeToken, string code, CancellationToken cancellationToken = default)
    {
        var user = await ValidateChallengeAsync(challengeToken, SetupPurpose, cancellationToken);
        if (user is null || user.MfaEnabledAtUtc is not null || string.IsNullOrWhiteSpace(user.MfaSecretEncrypted)) return null;
        var step = VerifyTotp(Decrypt(user.MfaSecretEncrypted), code, user.MfaLastUsedTimeStep);
        if (step is null) return null;

        var previousCodes = await db.MfaRecoveryCodes.Where(x => x.UserId == user.Id).ToListAsync(cancellationToken);
        db.MfaRecoveryCodes.RemoveRange(previousCodes);
        var recoveryCodes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => CreateRecoveryCode()).ToArray();
        foreach (var recoveryCode in recoveryCodes)
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = user.Id, CodeHash = HashRecoveryCode(recoveryCode), CreatedAtUtc = Now });

        user.MfaEnabledAtUtc = Now;
        user.MfaLastUsedTimeStep = step;
        user.TokenVersion++;
        await db.SaveChangesAsync(cancellationToken);
        return new MfaVerification(user, recoveryCodes);
    }

    public async Task<MfaVerification?> CompleteAsync(string challengeToken, string code, CancellationToken cancellationToken = default)
    {
        var user = await ValidateChallengeAsync(challengeToken, VerifyPurpose, cancellationToken);
        if (user is null || user.MfaEnabledAtUtc is null || string.IsNullOrWhiteSpace(user.MfaSecretEncrypted)) return null;

        var normalized = code.Trim();
        var accepted = false;
        if (normalized.Length == TotpDigits && normalized.All(char.IsAsciiDigit))
        {
            var step = VerifyTotp(Decrypt(user.MfaSecretEncrypted), normalized, user.MfaLastUsedTimeStep);
            if (step is not null)
            {
                user.MfaLastUsedTimeStep = step;
                accepted = true;
            }
        }
        else
        {
            var suppliedHash = HashRecoveryCode(normalized);
            var recoveryCodes = await db.MfaRecoveryCodes.Where(x => x.UserId == user.Id && x.UsedAtUtc == null).ToListAsync(cancellationToken);
            var match = recoveryCodes.FirstOrDefault(item => FixedTimeEquals(item.CodeHash, suppliedHash));
            if (match is not null)
            {
                match.UsedAtUtc = Now;
                accepted = true;
            }
        }

        if (!accepted) return null;
        await db.SaveChangesAsync(cancellationToken);
        return new MfaVerification(user, []);
    }

    private async Task<User?> ValidateChallengeAsync(string token, string purpose, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwt.Issuer,
                ValidAudience = _jwt.Audience + ".Mfa",
                IssuerSigningKey = SigningKey(),
                ClockSkew = TimeSpan.FromSeconds(30)
            }, out _);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return null;
        }

        if (!string.Equals(principal.FindFirst("mfa_purpose")?.Value, purpose, StringComparison.Ordinal) ||
            !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !int.TryParse(principal.FindFirst(JwtClaimNames.TokenVersion)?.Value, out var tokenVersion)) return null;

        var user = await db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return user is not null && user.IsActive && IsPrivileged(user.Role.Name) && user.TokenVersion == tokenVersion ? user : null;
    }

    private string CreateChallenge(User user, string purpose)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(JwtClaimNames.TokenVersion, user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("mfa_purpose", purpose),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(
            _jwt.Issuer,
            _jwt.Audience + ".Mfa",
            claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_mfa.ChallengeMinutes),
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(_jwt.Secret!));
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private long? VerifyTotp(byte[] secret, string suppliedCode, long? lastUsedStep)
    {
        var currentStep = timeProvider.GetUtcNow().ToUnixTimeSeconds() / TotpPeriodSeconds;
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = currentStep + offset;
            if (lastUsedStep is not null && step <= lastUsedStep.Value) continue;
            var expected = ComputeTotp(secret, step);
            if (FixedTimeEquals(expected, suppliedCode)) return step;
        }
        return null;
    }

    private static string ComputeTotp(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private string Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(Convert.FromBase64String(_mfa.EncryptionKey!), tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
    }

    private byte[] Decrypt(string encrypted)
    {
        var payload = Convert.FromBase64String(encrypted);
        if (payload.Length < 29) throw new CryptographicException("Invalid MFA secret payload.");
        var plaintext = new byte[payload.Length - 28];
        using var aes = new AesGcm(Convert.FromBase64String(_mfa.EncryptionKey!), 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), plaintext);
        return plaintext;
    }

    private static string CreateRecoveryCode()
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(10));
        return string.Join('-', Enumerable.Range(0, 5).Select(index => raw.Substring(index * 4, 4)));
    }

    private static string HashRecoveryCode(string code)
    {
        var normalized = new string(code.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static bool FixedTimeEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static bool IsPrivileged(string role) => role is "Admin" or "SuperAdmin";

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) output.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }
}
