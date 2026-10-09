namespace GymShop.Application.Abstractions;

public enum EmailSendFailureType
{
    HttpRejected,
    Timeout,
    Network
}

public sealed record EmailSendResult(
    bool AcceptedByProvider,
    string? DevelopmentCode = null,
    EmailSendFailureType? FailureType = null,
    int? ProviderStatusCode = null)
{
    public static EmailSendResult Accepted(string? developmentCode = null) => new(true, developmentCode);
    public static EmailSendResult Failed(EmailSendFailureType failureType, int? providerStatusCode = null) =>
        new(false, FailureType: failureType, ProviderStatusCode: providerStatusCode);
    public static EmailSendResult NotAttempted() => new(false);
}

public interface IVerificationEmailSender
{
    Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default);
}

public interface IPasswordResetEmailSender
{
    Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default);
}

public sealed record TransactionalEmailMessage(
    string Purpose,
    string Recipient,
    string Subject,
    string Html,
    string? IdempotencyKey = null,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public sealed record EmailAttachment(
    string FileName,
    string ContentType,
    byte[] Content);

public interface ITransactionalEmailSender
{
    Task<EmailSendResult> SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default);
}

public sealed record ExternalIdentity(
    string Provider,
    string Subject,
    string Email,
    bool EmailVerified,
    string FirstName,
    string? LastName,
    bool EmailAuthoritative = false);

public interface IExternalIdentityVerifier
{
    Task<ExternalIdentity?> VerifyGoogleAsync(string credential, CancellationToken cancellationToken = default);
}
