using System.Globalization;
using System.Security.Cryptography;
using GymShop.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.Common;

public static class BankTransferReference
{
    private const int MaxGenerationAttempts = 10;

    public static async Task<string> CreateUniqueAsync(
        IApplicationDbContext db,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var reference = Create();
            if (!await db.Payments.AnyAsync(x => x.ExternalReference == reference, cancellationToken)) return reference;
        }

        throw new InvalidOperationException("No se pudo generar una referencia bancaria única.");
    }

    private static string Create() =>
        RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000)
            .ToString(CultureInfo.InvariantCulture);
}
