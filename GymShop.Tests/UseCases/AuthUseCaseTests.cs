using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Auth;
using GymShop.Application.UseCases.Auth;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;

namespace GymShop.Tests.UseCases;

public class AuthUseCaseTests
{
    [Fact]
    public async Task Register_creates_unverified_user_and_sends_code()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        var result = await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "CLIENTE@TEST.COM", "clave123"));
        Assert.True(result.IsSuccess); Assert.Equal("cliente@test.com", result.Value!.Email); Assert.Equal(60, result.Value.ExpiresInSeconds);
        Assert.Single(db.Users); Assert.Null(db.Users.Single().EmailVerifiedAt); Assert.Single(db.EmailVerificationCodes); Assert.NotNull(sender.Code);
    }

    [Fact]
    public async Task Register_send_failure_removes_pending_account_and_allows_retry()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var sender = new FakeSender { Result = EmailSendResult.Failed(EmailSendFailureType.HttpRejected, 422) };
        var register = Register(db, sender);
        var request = new RegisterRequest("Cliente", "Test", "retry@test.com", "clave123");

        var failed = await register.ExecuteAsync(request);

        Assert.False(failed.IsSuccess); Assert.Equal(AppErrorType.Unavailable, failed.Error!.Type);
        Assert.Empty(db.Users); Assert.Empty(db.EmailVerificationCodes);

        sender.Result = EmailSendResult.Accepted();
        var retried = await register.ExecuteAsync(request);
        Assert.True(retried.IsSuccess); Assert.Single(db.Users); Assert.Single(db.EmailVerificationCodes);
    }

    [Fact]
    public async Task Verify_valid_code_marks_email_and_returns_token()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        var result = await new VerifyEmailUseCase(db, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new VerifyEmailRequest("cliente@test.com", sender.Code!));
        Assert.True(result.IsSuccess); Assert.StartsWith("test-token-for-", result.Value!.Token); Assert.NotNull(db.Users.Single().EmailVerifiedAt); Assert.NotNull(db.EmailVerificationCodes.Single().ConsumedAtUtc);
    }

    [Fact]
    public async Task Verify_rejects_invalid_code_and_counts_attempt()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        var result = await new VerifyEmailUseCase(db, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new VerifyEmailRequest("cliente@test.com", "999999"));
        Assert.False(result.IsSuccess); Assert.Equal("El codigo no es valido, vencio o ya fue utilizado.", result.Error!.Message); Assert.Equal(1, db.EmailVerificationCodes.Single().FailedAttempts);
    }

    [Fact]
    public async Task Verify_rejects_expired_code()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        db.EmailVerificationCodes.Single().ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        var result = await new VerifyEmailUseCase(db, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new VerifyEmailRequest("cliente@test.com", sender.Code!));
        Assert.False(result.IsSuccess); Assert.Equal("El codigo no es valido, vencio o ya fue utilizado.", result.Error!.Message); Assert.Null(db.Users.Single().EmailVerifiedAt);
    }

    [Fact]
    public async Task Resend_is_indistinguishable_while_current_code_is_valid()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        var result = await new ResendVerificationUseCase(db, sender, TimeProvider.System).ExecuteAsync(new ResendVerificationRequest("cliente@test.com"));
        Assert.True(result.IsSuccess); Assert.Null(result.Value!.DevelopmentCode); Assert.Single(db.EmailVerificationCodes);
    }

    [Fact]
    public async Task Failed_verification_resend_consumes_code_and_can_be_retried_immediately()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender();
        await Register(db, sender).ExecuteAsync(new RegisterRequest("Cliente", "Test", "resend@test.com", "clave123"));
        db.EmailVerificationCodes.Single().ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        var resend = new ResendVerificationUseCase(db, sender, TimeProvider.System);
        sender.Result = EmailSendResult.Failed(EmailSendFailureType.Timeout);

        var failed = await resend.ExecuteAsync(new ResendVerificationRequest("resend@test.com"));

        Assert.False(failed.IsSuccess); Assert.Equal(AppErrorType.Unavailable, failed.Error!.Type);
        Assert.All(db.EmailVerificationCodes, item => Assert.NotNull(item.ConsumedAtUtc));

        sender.Result = EmailSendResult.Accepted();
        var retried = await resend.ExecuteAsync(new ResendVerificationRequest("resend@test.com"));
        Assert.True(retried.IsSuccess);
    }

    [Fact]
    public async Task Login_rejects_user_until_email_is_verified()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender(); var hasher = new PasswordHasher();
        await new RegisterUserUseCase(db, hasher, sender, TimeProvider.System).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        var result = await new LoginUserUseCase(db, hasher, new FakeJwtTokenService()).ExecuteAsync(new LoginRequest("cliente@test.com", "clave123"));
        Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Login_succeeds_after_verification()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender(); var hasher = new PasswordHasher();
        await new RegisterUserUseCase(db, hasher, sender, TimeProvider.System).ExecuteAsync(new RegisterRequest("Cliente", "Test", "cliente@test.com", "clave123"));
        await new VerifyEmailUseCase(db, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new VerifyEmailRequest("cliente@test.com", sender.Code!));
        var result = await new LoginUserUseCase(db, hasher, new FakeJwtTokenService()).ExecuteAsync(new LoginRequest("cliente@test.com", "clave123"));
        Assert.True(result.IsSuccess); Assert.Equal("Test", result.Value!.User.LastName);
    }

    [Fact]
    public async Task Login_progressively_upgrades_legacy_password_hash()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "legacy@test.com", "clave123");
        user.PasswordHash = GymShop.Tests.Services.PasswordHasherTests.LegacyHash("clave123");
        await db.SaveChangesAsync();

        var result = await new LoginUserUseCase(db, hasher, new FakeJwtTokenService())
            .ExecuteAsync(new LoginRequest("legacy@test.com", "clave123"));

        Assert.True(result.IsSuccess);
        Assert.True(hasher.Verify("clave123", user.PasswordHash));
        Assert.False(hasher.NeedsRehash(user.PasswordHash));
        Assert.Equal("600000", user.PasswordHash.Split('.')[2]);
    }

    [Fact]
    public async Task Register_hides_duplicate_email_case_insensitively()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakeSender(); var register = Register(db, sender);
        await register.ExecuteAsync(new RegisterRequest("Cliente", "Test", "Cliente@Test.com", "clave123"));
        var result = await register.ExecuteAsync(new RegisterRequest("Otro", "Test", "cliente@test.com", "clave123"));
        Assert.True(result.IsSuccess); Assert.Null(result.Value!.DevelopmentCode); Assert.Single(db.Users);
    }

    [Fact]
    public async Task Google_login_creates_new_user_with_user_role()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "google-sub-new", "NEW@TEST.COM", true, "Nueva", "Persona"));
        var result = await new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new GoogleLoginRequest("credential"));
        Assert.True(result.IsSuccess); Assert.Equal("User", result.Value!.User.Role); Assert.Equal("new@test.com", result.Value.User.Email);
        Assert.Single(db.Users); Assert.Single(db.UserExternalLogins); Assert.NotNull(db.Users.Single().EmailVerifiedAt);
    }

    [Fact]
    public async Task Google_login_recognizes_existing_user_by_google_subject()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "stable-subject", "member@test.com", true, "Member", null));
        var login = new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System);
        var created = await login.ExecuteAsync(new GoogleLoginRequest("credential"));

        var existing = await login.ExecuteAsync(new GoogleLoginRequest("another-credential"));

        Assert.True(created.IsSuccess); Assert.True(existing.IsSuccess); Assert.Equal(created.Value!.User.Id, existing.Value!.User.Id);
        Assert.Single(db.Users); Assert.Single(db.UserExternalLogins);
    }

    [Fact]
    public async Task Google_login_requires_authenticated_local_session_before_linking_matching_email()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "cliente@test.com", "clave123");
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "google-sub-link", "CLIENTE@TEST.COM", true, "Cliente", "Google"));
        var login = new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System);

        var unauthenticated = await login.ExecuteAsync(new GoogleLoginRequest("credential"));
        var linked = await login.ExecuteAsync(new GoogleLoginRequest("credential"), user.Id);

        Assert.False(unauthenticated.IsSuccess); Assert.Equal("google_link_required", unauthenticated.Error!.Code);
        Assert.True(linked.IsSuccess); Assert.Single(db.Users); Assert.Single(db.UserExternalLogins);
    }

    [Fact]
    public async Task Google_login_automatically_links_existing_account_when_google_is_email_authority()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "cliente@gmail.com", "clave123");
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "gmail-subject", "CLIENTE@GMAIL.COM", true, "Cliente", null, true));

        var result = await new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new GoogleLoginRequest("credential"));

        Assert.True(result.IsSuccess); Assert.Equal(user.Id, result.Value!.User.Id); Assert.Single(db.Users); Assert.Single(db.UserExternalLogins);
        Assert.True(hasher.Verify("clave123", db.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task Google_login_rejects_deactivated_linked_account()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "disabled-subject", "disabled@test.com", true, "Disabled", null));
        var login = new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System);
        var created = await login.ExecuteAsync(new GoogleLoginRequest("credential"));
        db.Users.Single().IsActive = false; await db.SaveChangesAsync();

        var result = await login.ExecuteAsync(new GoogleLoginRequest("credential"));

        Assert.True(created.IsSuccess); Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Unauthorized, result.Error!.Type);
    }

    [Fact]
    public async Task Google_login_cannot_take_over_admin_by_matching_email()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var adminRole = db.Roles.Single(x => x.Name == "Admin");
        db.Users.Add(new User { Email = "admin@test.com", Name = "Admin", PasswordHash = "hash", Role = adminRole, RoleId = adminRole.Id, IsActive = true, EmailVerifiedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "attacker-subject", "admin@test.com", true, "Attacker", null));

        var result = await new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new GoogleLoginRequest("credential"));

        Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Conflict, result.Error!.Type); Assert.Empty(db.UserExternalLogins);
        Assert.Equal("Admin", db.Users.Single().Role.Name);
    }

    [Fact]
    public async Task Google_login_rejects_unverified_email()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var verifier = new FakeExternalVerifier(new ExternalIdentity("Google", "google-sub-2", "unsafe@test.com", false, "Unsafe", null));
        var result = await new GoogleLoginUseCase(db, verifier, new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new GoogleLoginRequest("credential"));
        Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Unauthorized, result.Error!.Type); Assert.Empty(db.Users);
    }

    [Fact]
    public async Task Google_login_rejects_invalid_credential()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var result = await new GoogleLoginUseCase(db, new FakeExternalVerifier(null), new FakeJwtTokenService(), TimeProvider.System).ExecuteAsync(new GoogleLoginRequest("invalid"));
        Assert.False(result.IsSuccess); Assert.Equal(AppErrorType.Unauthorized, result.Error!.Type); Assert.Empty(db.Users);
    }

    [Fact]
    public async Task Password_reset_request_is_generic_for_existing_and_unknown_email()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakePasswordResetSender(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "known@test.com", "clave123");
        var useCase = new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System);

        var known = await useCase.ExecuteAsync(new RequestPasswordResetRequest("KNOWN@TEST.COM"));
        var knownCode = sender.Code;
        var unknown = await useCase.ExecuteAsync(new RequestPasswordResetRequest("unknown@test.com"));

        Assert.True(known.IsSuccess); Assert.True(unknown.IsSuccess);
        Assert.Equal(known.Value!.Message, unknown.Value!.Message);
        Assert.Equal(600, known.Value.ExpiresInSeconds); Assert.Equal(600, unknown.Value.ExpiresInSeconds);
        Assert.Matches("^[0-9]{6}$", knownCode!); Assert.Matches("^[0-9]{6}$", sender.Code!);
        Assert.Single(db.PasswordResetCodes); Assert.Equal(user.Id, db.PasswordResetCodes.Single().UserId);
        Assert.DoesNotContain(knownCode!, db.PasswordResetCodes.Single().CodeHash);
    }

    [Fact]
    public async Task Password_reset_send_failure_stays_generic_and_invalidates_unsent_code()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var sender = new FakePasswordResetSender { Result = EmailSendResult.Failed(EmailSendFailureType.Network) };
        var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "delivery-failure@test.com", "clave123");
        var useCase = new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System);

        var existing = await useCase.ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        var unknown = await useCase.ExecuteAsync(new RequestPasswordResetRequest("unknown@test.com"));

        Assert.True(existing.IsSuccess); Assert.True(unknown.IsSuccess);
        Assert.Equal(existing.Value!.Message, unknown.Value!.Message);
        Assert.Null(existing.Value.DevelopmentCode); Assert.Null(unknown.Value.DevelopmentCode);
        Assert.NotNull(db.PasswordResetCodes.Single().ConsumedAtUtc);
    }

    [Fact]
    public async Task Failed_password_reset_resend_keeps_previous_code_usable()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var sender = new FakePasswordResetSender();
        var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "preserve-reset@test.com", "clave123");
        var request = new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System);

        await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        var previousCode = sender.Code!;
        sender.Result = EmailSendResult.Failed(EmailSendFailureType.HttpRejected, 422);
        var retryResponse = await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email));

        Assert.True(retryResponse.IsSuccess);
        Assert.Equal(2, db.PasswordResetCodes.Count());
        Assert.Single(db.PasswordResetCodes.Where(item => item.ConsumedAtUtc == null));
        Assert.True(hasher.Verify(previousCode, db.PasswordResetCodes.Single(item => item.ConsumedAtUtc == null).CodeHash));

        var reset = await new ConfirmPasswordResetUseCase(db, hasher, TimeProvider.System)
            .ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, previousCode, "nuevaClave456"));
        Assert.True(reset.IsSuccess);
    }

    [Fact]
    public async Task Pending_password_reset_delivery_does_not_shadow_previous_code()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var acceptedSender = new FakePasswordResetSender();
        var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "pending-delivery@test.com", "clave123");
        await new RequestPasswordResetUseCase(db, acceptedSender, hasher, TimeProvider.System)
            .ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        var previousCode = acceptedSender.Code!;
        var pendingSender = new PendingPasswordResetSender();
        var pendingRequest = new RequestPasswordResetUseCase(db, pendingSender, hasher, TimeProvider.System)
            .ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        await pendingSender.Started;

        Assert.Equal(2, db.PasswordResetCodes.Count());
        Assert.Single(db.PasswordResetCodes.Where(item => item.ConsumedAtUtc == null));
        var reset = await new ConfirmPasswordResetUseCase(db, hasher, TimeProvider.System)
            .ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, previousCode, "nuevaClave456"));
        Assert.True(reset.IsSuccess);

        // Even an accepted delivery must not revive a request from before the
        // password change that completed while the provider response was pending.
        pendingSender.Complete(EmailSendResult.Accepted());
        var response = await pendingRequest;
        Assert.True(response.IsSuccess);
        Assert.All(db.PasswordResetCodes, item => Assert.NotNull(item.ConsumedAtUtc));
    }

    [Fact]
    public async Task Password_reset_changes_password_consumes_code_and_increments_token_version()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakePasswordResetSender(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "reset@test.com", "clave123");
        await new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System).ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        var tokenVersion = user.TokenVersion;

        var result = await new ConfirmPasswordResetUseCase(db, hasher, TimeProvider.System).ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, sender.Code!, "nuevaClave456"));

        Assert.True(result.IsSuccess); Assert.True(hasher.Verify("nuevaClave456", user.PasswordHash)); Assert.False(hasher.Verify("clave123", user.PasswordHash));
        Assert.Equal(tokenVersion + 1, user.TokenVersion); Assert.NotNull(db.PasswordResetCodes.Single().ConsumedAtUtc);
        var oldLogin = await new LoginUserUseCase(db, hasher, new FakeJwtTokenService()).ExecuteAsync(new LoginRequest(user.Email, "clave123"));
        var newLogin = await new LoginUserUseCase(db, hasher, new FakeJwtTokenService()).ExecuteAsync(new LoginRequest(user.Email, "nuevaClave456"));
        Assert.False(oldLogin.IsSuccess); Assert.True(newLogin.IsSuccess);
    }

    [Fact]
    public async Task Password_reset_code_is_single_use_and_resend_invalidates_previous_code()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakePasswordResetSender(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "single@test.com", "clave123");
        var request = new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System);
        await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email)); var firstCode = sender.Code!;
        await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email)); var secondCode = sender.Code!;
        var confirm = new ConfirmPasswordResetUseCase(db, hasher, TimeProvider.System);

        Assert.Single(db.PasswordResetCodes.Where(item => item.ConsumedAtUtc == null));
        Assert.True(hasher.Verify(secondCode, db.PasswordResetCodes.Single(item => item.ConsumedAtUtc == null).CodeHash));
        Assert.False((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, firstCode, "nuevaClave456"))).IsSuccess);
        Assert.True((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, secondCode, "nuevaClave456"))).IsSuccess);
        Assert.False((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, secondCode, "otraClave789"))).IsSuccess);
        Assert.Equal(2, db.PasswordResetCodes.Count()); Assert.All(db.PasswordResetCodes, code => Assert.NotNull(code.ConsumedAtUtc));
    }

    [Fact]
    public async Task Password_reset_rejects_expired_code_and_limits_failed_attempts()
    {
        await using var db = await TestDbContextFactory.CreateAsync(); var sender = new FakePasswordResetSender(); var hasher = new PasswordHasher();
        var user = await SeedVerifiedUserAsync(db, hasher, "attempts@test.com", "clave123");
        var request = new RequestPasswordResetUseCase(db, sender, hasher, TimeProvider.System);
        var confirm = new ConfirmPasswordResetUseCase(db, hasher, TimeProvider.System);
        await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email));
        db.PasswordResetCodes.Single().ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
        Assert.False((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, sender.Code!, "nuevaClave456"))).IsSuccess);

        await request.ExecuteAsync(new RequestPasswordResetRequest(user.Email)); var validCode = sender.Code!;
        for (var attempt = 0; attempt < 5; attempt++) Assert.False((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, "999999", "nuevaClave456"))).IsSuccess);
        Assert.False((await confirm.ExecuteAsync(new ConfirmPasswordResetRequest(user.Email, validCode, "nuevaClave456"))).IsSuccess);
        Assert.Equal(5, db.PasswordResetCodes.OrderByDescending(x => x.Id).First().FailedAttempts);
    }

    private static async Task<User> SeedVerifiedUserAsync(IApplicationDbContext db, PasswordHasher hasher, string email, string password)
    {
        var role = db.Roles.Single(x => x.Name == "User");
        var user = new User { Email = email, Name = "Reset", PasswordHash = hasher.Hash(password), RoleId = role.Id, Role = role, IsActive = true, EmailVerifiedAt = DateTime.UtcNow };
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }

    private static RegisterUserUseCase Register(IApplicationDbContext db, FakeSender sender) => new(db, new PasswordHasher(), sender, TimeProvider.System);
    private sealed class FakeSender : IVerificationEmailSender
    {
        public string? Code { get; private set; }
        public EmailSendResult Result { get; set; } = EmailSendResult.Accepted();
        public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
        {
            if (!deliver) return Task.FromResult(EmailSendResult.NotAttempted());
            Code = code;
            return Task.FromResult(Result.AcceptedByProvider ? EmailSendResult.Accepted(code) : Result);
        }
    }
    private sealed class FakePasswordResetSender : IPasswordResetEmailSender
    {
        public string? Code { get; private set; }
        public EmailSendResult Result { get; set; } = EmailSendResult.Accepted();
        public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
        {
            if (!deliver) return Task.FromResult(EmailSendResult.NotAttempted());
            Code = code;
            return Task.FromResult(Result.AcceptedByProvider ? EmailSendResult.Accepted(code) : Result);
        }
    }
    private sealed class PendingPasswordResetSender : IPasswordResetEmailSender
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<EmailSendResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public Task<EmailSendResult> SendAsync(string email, string code, bool deliver = true, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            return _completion.Task;
        }
        public void Complete(EmailSendResult result) => _completion.TrySetResult(result);
    }
    private sealed class FakeExternalVerifier(ExternalIdentity? identity) : IExternalIdentityVerifier { public Task<ExternalIdentity?> VerifyGoogleAsync(string credential, CancellationToken cancellationToken = default) => Task.FromResult(identity); }
}
