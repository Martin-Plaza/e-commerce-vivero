using System.Net.Http.Json;
using Google.Apis.Auth;
using GymShop.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GymShop.Infrastructure.Configuration;

namespace GymShop.Infrastructure.Services;

public sealed class MockVerificationEmailSender(
    ILogger<MockVerificationEmailSender> logger,
    IOptions<EmailOptions> options) : IVerificationEmailSender
{
    public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Mock verification email generated for {Email}.", email);
        return Task.FromResult(EmailSendResult.Accepted(options.Value.ExposeDevelopmentCodes ? code : null));
    }
}

public sealed class MockPasswordResetEmailSender(
    ILogger<MockPasswordResetEmailSender> logger,
    IOptions<EmailOptions> options) : IPasswordResetEmailSender
{
    public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Mock password-reset email generated for {Email}.", email);
        return Task.FromResult(EmailSendResult.Accepted(options.Value.ExposeDevelopmentCodes ? code : null));
    }
}

public sealed class MockTransactionalEmailSender(ILogger<MockTransactionalEmailSender> logger) : ITransactionalEmailSender
{
    public Task<EmailSendResult> SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Mock transactional email accepted. Purpose {Purpose}.", message.Purpose);
        return Task.FromResult(EmailSendResult.Accepted());
    }
}

public sealed class ResendEmailSender(
    HttpClient client,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IVerificationEmailSender, IPasswordResetEmailSender, ITransactionalEmailSender
{
    private readonly EmailOptions _options = options.Value;

    Task<EmailSendResult> IVerificationEmailSender.SendAsync(string email, string code, bool deliver, CancellationToken cancellationToken) =>
        deliver ? SendAsync(email, "verification", "Verificá tu email en GymShop", "Código de verificación", code, cancellationToken) : Task.FromResult(EmailSendResult.NotAttempted());

    Task<EmailSendResult> IPasswordResetEmailSender.SendAsync(string email, string code, bool deliver, CancellationToken cancellationToken) =>
        deliver ? SendAsync(email, "password-reset", "Recuperá tu contraseña de GymShop", "Código de recuperación", code, cancellationToken) : Task.FromResult(EmailSendResult.NotAttempted());

    Task<EmailSendResult> ITransactionalEmailSender.SendAsync(TransactionalEmailMessage message, CancellationToken cancellationToken) =>
        SendHtmlAsync(message.Recipient, message.Purpose, message.Subject, message.Html, cancellationToken, message.IdempotencyKey, message.Attachments);

    private async Task<EmailSendResult> SendAsync(string email, string purpose, string subject, string heading, string code, CancellationToken cancellationToken)
        => await SendHtmlAsync(email, purpose, subject,
            $"<h1>{heading}</h1><p>Tu código es:</p><p style=\"font-size:28px;font-weight:700;letter-spacing:6px\">{code}</p><p>Si no solicitaste este mensaje, podés ignorarlo.</p>", cancellationToken, null);

    private async Task<EmailSendResult> SendHtmlAsync(string email, string purpose, string subject, string html, CancellationToken cancellationToken, string? idempotencyKey, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var from = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress
            : $"{_options.FromName} <{_options.FromAddress}>";
        var payload = new Dictionary<string, object>
        {
            ["from"] = from,
            ["to"] = new[] { email },
            ["subject"] = subject,
            ["html"] = html
        };
        if (attachments is { Count: > 0 })
            payload["attachments"] = attachments.Select(attachment => new
            {
                filename = attachment.FileName,
                content = Convert.ToBase64String(attachment.Content),
                content_type = attachment.ContentType
            }).ToArray();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "emails") { Content = JsonContent.Create(payload) };
            if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Transactional email request failed. Provider {Provider}; Purpose {Purpose}; FailureType {FailureType}; StatusCode {StatusCode}.",
                    "Resend", purpose, EmailSendFailureType.HttpRejected, (int)response.StatusCode);
                return EmailSendResult.Failed(EmailSendFailureType.HttpRejected, (int)response.StatusCode);
            }

            logger.LogInformation("Transactional email request accepted by provider. Provider {Provider}; Purpose {Purpose}; StatusCode {StatusCode}.",
                "Resend", purpose, (int)response.StatusCode);
            return EmailSendResult.Accepted();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Transactional email request failed. Provider {Provider}; Purpose {Purpose}; FailureType {FailureType}.",
                "Resend", purpose, EmailSendFailureType.Timeout);
            return EmailSendResult.Failed(EmailSendFailureType.Timeout);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError("Transactional email request failed. Provider {Provider}; Purpose {Purpose}; FailureType {FailureType}; ExceptionType {ExceptionType}.",
                "Resend", purpose, EmailSendFailureType.Network, exception.GetType().Name);
            return EmailSendResult.Failed(EmailSendFailureType.Network);
        }
    }
}

public sealed class GoogleIdentityVerifier(IConfiguration configuration) : IExternalIdentityVerifier
{
    public async Task<ExternalIdentity?> VerifyGoogleAsync(string credential, CancellationToken cancellationToken = default)
    {
        var clientId = configuration["GoogleAuth:ClientId"];
        if (string.IsNullOrWhiteSpace(credential) || string.IsNullOrWhiteSpace(clientId)) return null;

        try
        {
            var token = await GoogleJsonWebSignature.ValidateAsync(credential, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId]
            });
            cancellationToken.ThrowIfCancellationRequested();
            if (!token.EmailVerified || string.IsNullOrWhiteSpace(token.Subject) || string.IsNullOrWhiteSpace(token.Email)) return null;
            var emailAuthoritative = token.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(token.HostedDomain);
            return new ExternalIdentity(
                "Google",
                token.Subject,
                token.Email,
                true,
                token.GivenName ?? token.Email.Split('@')[0],
                token.FamilyName,
                emailAuthoritative);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
