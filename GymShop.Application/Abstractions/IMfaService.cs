using GymShop.Domain.Entities;

namespace GymShop.Application.Abstractions;

public sealed record MfaLoginRequirement(User User, bool SetupRequired, string ChallengeToken);
public sealed record MfaSetupData(string SharedKey, string OtpAuthUri, IReadOnlyList<string> QrCodeRows);
public sealed record MfaVerification(User User, IReadOnlyList<string> RecoveryCodes);

public interface IMfaService
{
    Task<MfaLoginRequirement?> CreateLoginRequirementAsync(int userId, CancellationToken cancellationToken = default);
    Task<MfaSetupData?> BeginSetupAsync(string challengeToken, CancellationToken cancellationToken = default);
    Task<MfaVerification?> EnableAsync(string challengeToken, string code, CancellationToken cancellationToken = default);
    Task<MfaVerification?> CompleteAsync(string challengeToken, string code, CancellationToken cancellationToken = default);
}
