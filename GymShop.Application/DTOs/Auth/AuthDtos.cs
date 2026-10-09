using System.ComponentModel.DataAnnotations;
using GymShop.Application.Common;
using System.Text.Json.Serialization;

namespace GymShop.Application.DTOs.Auth;

public record RegisterRequest(
    [Required, StringLength(ValidationLimits.UserName)] string Name,
    [Required, StringLength(ValidationLimits.UserName)] string LastName,
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email,
    [Required, StrongPassword] string Password);

public record RegistrationPendingResponse(
    string Email,
    int ExpiresInSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevelopmentCode);

public record VerifyEmailRequest(
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

public record ResendVerificationRequest(
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email);

public record GoogleLoginRequest([Required] string Credential);

public record LoginRequest(
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email,
    [Required] string Password);

public record RequestPasswordResetRequest(
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email);

public record PasswordResetPendingResponse(
    string Message,
    int ExpiresInSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevelopmentCode);

public record ConfirmPasswordResetRequest(
    [Required, EmailAddress, StringLength(ValidationLimits.Email)] string Email,
    [Required, RegularExpression("^[0-9]{6}$")] string Code,
    [Required, StrongPassword] string NewPassword);

public record PasswordResetCompletedResponse(string Message);

public record AuthResponse(
    [property: JsonIgnore] string Token,
    UserResponse User);

public record MfaChallengeResponse(bool MfaRequired, bool SetupRequired, UserResponse User);
public record MfaCodeRequest([Required, StringLength(32, MinimumLength = 6)] string Code);
public record MfaSetupResponse(string SharedKey, string OtpAuthUri, IReadOnlyList<string> QrCodeRows);
public record MfaCompletedResponse(UserResponse User, IReadOnlyList<string> RecoveryCodes);

public record UserResponse(int Id, string Email, string Name, string? LastName, string Role);

