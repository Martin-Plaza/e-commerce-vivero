using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Auth;
using GymShop.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GymShop.Application.UseCases.Auth;

//task: asincronico
//AppResult: puede terminar con exito o error controlado.

public interface IRegisterUserUseCase { Task<AppResult<RegistrationPendingResponse>> ExecuteAsync(RegisterRequest request, CancellationToken cancellationToken = default); }
public interface IVerifyEmailUseCase { Task<AppResult<AuthResponse>> ExecuteAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default); }
public interface IResendVerificationUseCase { Task<AppResult<RegistrationPendingResponse>> ExecuteAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default); }
public interface IGoogleLoginUseCase { Task<AppResult<AuthResponse>> ExecuteAsync(GoogleLoginRequest request, int? linkingUserId = null, CancellationToken cancellationToken = default); }
public interface ILoginUserUseCase { Task<AppResult<AuthResponse>> ExecuteAsync(LoginRequest request, CancellationToken cancellationToken = default); }
public interface IRequestPasswordResetUseCase { Task<AppResult<PasswordResetPendingResponse>> ExecuteAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken = default); }
public interface IConfirmPasswordResetUseCase { Task<AppResult<PasswordResetCompletedResponse>> ExecuteAsync(ConfirmPasswordResetRequest request, CancellationToken cancellationToken = default); }
public interface IGetCurrentUserUseCase { Task<AppResult<UserResponse>> ExecuteAsync(int userId, CancellationToken cancellationToken = default); }

//clase para crear un user en DTO
internal static class AuthMapping
{
    public static UserResponse User(User user) => new(user.Id, user.Email, user.Name, user.LastName, user.Role.Name);
    public static AuthResponse Auth(User user, IJwtTokenService jwt) => new(jwt.CreateToken(user), User(user));
}

public sealed class RegisterUserUseCase : IRegisterUserUseCase
{

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IVerificationEmailSender _sender;
    private readonly TimeProvider _time;

    public RegisterUserUseCase(IApplicationDbContext db, IPasswordHasher hasher, IVerificationEmailSender sender, TimeProvider time) => (_db, _hasher, _sender, _time) = (db, hasher, sender, time);
    //recibe: registerRequest - name, lastname, email y passoword, como variable request
    //devuelve?: registrationPendingResponse - nuevo usuario, llamado de la db, tiempo y sender
        public async Task<AppResult<RegistrationPendingResponse>> ExecuteAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var name = request.Name.Trim();
        var lastName = request.LastName.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(lastName) || email.Length > ValidationLimits.Email || !new EmailAddressAttribute().IsValid(email))
            return AppResult<RegistrationPendingResponse>.Failure(AppErrorType.Validation, "Nombre, apellido y email valido son obligatorios.");
        if (!new StrongPasswordAttribute().IsValid(request.Password))
            return AppResult<RegistrationPendingResponse>.Failure(AppErrorType.Validation, "La password debe tener entre 8 y 128 caracteres e incluir al menos una letra y un numero.");
        if (await _db.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            _ = _hasher.Hash(request.Password); // Reduce la diferencia temporal con un alta real.
            return AppResult<RegistrationPendingResponse>.Success(new(email, Verification.LifetimeSeconds, null));
        }

        var role = await _db.Roles.SingleAsync(x => x.Name == "User", cancellationToken);
        var user = new User {
            Email = email,
            Name = name,
            LastName = lastName,
            PasswordHash = _hasher.Hash(request.Password),
            RoleId = role.Id,
            Role = role,
            IsActive = true };
        _db.Users.Add(user);

        var result = await Verification.CreateAsync(_db, _sender, _time, user, cancellationToken);
        if (!result.IsSuccess)
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return result;
    }
}

internal static class Verification
{
    public const int LifetimeSeconds = 60;
    public const string SendFailureMessage = "No pudimos solicitar el envío del código. Intentá nuevamente.";
    public static async Task<AppResult<RegistrationPendingResponse>> CreateAsync(IApplicationDbContext db, IVerificationEmailSender sender, TimeProvider time, User user, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var active = await db.EmailVerificationCodes.Where(x => x.UserId == user.Id && x.ConsumedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var item in active) item.ConsumedAtUtc = now;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var verification = new EmailVerificationCode { User = user, UserId = user.Id, CodeHash = Hash(code), CreatedAtUtc = now, ExpiresAtUtc = now.AddSeconds(LifetimeSeconds) };
        db.EmailVerificationCodes.Add(verification);
        await db.SaveChangesAsync(cancellationToken);
        var send = await sender.SendAsync(user.Email, code, cancellationToken: cancellationToken);
        if (!send.AcceptedByProvider)
        {
            verification.ConsumedAtUtc = time.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return AppResult<RegistrationPendingResponse>.Failure(AppErrorType.Unavailable, SendFailureMessage, "email_send_failed");
        }
        return AppResult<RegistrationPendingResponse>.Success(new(user.Email, LifetimeSeconds, send.DevelopmentCode));
    }
    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}

public sealed class VerifyEmailUseCase : IVerifyEmailUseCase
{
    private const string GenericInvalidMessage = "El codigo no es valido, vencio o ya fue utilizado.";
    private readonly IApplicationDbContext _db; private readonly IJwtTokenService _jwt; private readonly TimeProvider _time;
    public VerifyEmailUseCase(IApplicationDbContext db, IJwtTokenService jwt, TimeProvider time) => (_db, _jwt, _time) = (db, jwt, time);
    public async Task<AppResult<AuthResponse>> ExecuteAsync(VerifyEmailRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant(); var now = _time.GetUtcNow().UtcDateTime;
        var user = await _db.Users.Include(x => x.Role).Include(x => x.EmailVerificationCodes).SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null) return AppResult<AuthResponse>.Failure(AppErrorType.Validation, GenericInvalidMessage);
        var verification = user.EmailVerificationCodes.Where(x => x.ConsumedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (verification is null || verification.ExpiresAtUtc <= now || verification.FailedAttempts >= 5)
            return AppResult<AuthResponse>.Failure(AppErrorType.Validation, GenericInvalidMessage);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(verification.CodeHash), Convert.FromHexString(Verification.Hash(request.Code))))
        { verification.FailedAttempts++; await _db.SaveChangesAsync(cancellationToken); return AppResult<AuthResponse>.Failure(AppErrorType.Validation, GenericInvalidMessage); }
        verification.ConsumedAtUtc = now; user.EmailVerifiedAt = now; user.TokenVersion++;
        await _db.SaveChangesAsync(cancellationToken);
        return AppResult<AuthResponse>.Success(AuthMapping.Auth(user, _jwt));
    }
}

public sealed class ResendVerificationUseCase : IResendVerificationUseCase
{
    private readonly IApplicationDbContext _db; private readonly IVerificationEmailSender _sender; private readonly TimeProvider _time;
    public ResendVerificationUseCase(IApplicationDbContext db, IVerificationEmailSender sender, TimeProvider time) => (_db, _sender, _time) = (db, sender, time);
    public async Task<AppResult<RegistrationPendingResponse>> ExecuteAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant(); var user = await _db.Users.SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || user.EmailVerifiedAt is not null)
            return AppResult<RegistrationPendingResponse>.Success(new(email, Verification.LifetimeSeconds, null));
        var now = _time.GetUtcNow().UtcDateTime;
        if (await _db.EmailVerificationCodes.AnyAsync(x => x.UserId == user.Id && x.ConsumedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken))
            return AppResult<RegistrationPendingResponse>.Success(new(email, Verification.LifetimeSeconds, null));
        return await Verification.CreateAsync(_db, _sender, _time, user, cancellationToken);
    }
}

public sealed class GoogleLoginUseCase : IGoogleLoginUseCase
{
    private readonly IApplicationDbContext _db; private readonly IExternalIdentityVerifier _verifier; private readonly IJwtTokenService _jwt; private readonly TimeProvider _time;
    public GoogleLoginUseCase(IApplicationDbContext db, IExternalIdentityVerifier verifier, IJwtTokenService jwt, TimeProvider time) => (_db, _verifier, _jwt, _time) = (db, verifier, jwt, time);
    public async Task<AppResult<AuthResponse>> ExecuteAsync(GoogleLoginRequest request, int? linkingUserId = null, CancellationToken cancellationToken = default)
    {
        var identity = await _verifier.VerifyGoogleAsync(request.Credential, cancellationToken);
        if (identity is null || !identity.EmailVerified) return AppResult<AuthResponse>.Failure(AppErrorType.Unauthorized, "La credencial de Google no es valida.");
        var external = await _db.UserExternalLogins.Include(x => x.User).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Provider == identity.Provider && x.ProviderSubject == identity.Subject, cancellationToken);
        if (external is not null) return external.User.IsActive ? AppResult<AuthResponse>.Success(AuthMapping.Auth(external.User, _jwt)) : AppResult<AuthResponse>.Failure(AppErrorType.Unauthorized, "La cuenta no esta activa.");
        var email = identity.Email.Trim().ToLowerInvariant();
        var user = linkingUserId is null
            ? await _db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Email == email, cancellationToken)
            : await _db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == linkingUserId.Value, cancellationToken);
        if (user is null)
        {
            if (linkingUserId is not null)
                return AppResult<AuthResponse>.Failure(AppErrorType.Unauthorized, "La sesion local no es valida.");
            var role = await _db.Roles.SingleAsync(x => x.Name == "User", cancellationToken);
            user = new User { Email = email, Name = identity.FirstName, LastName = identity.LastName, PasswordHash = string.Empty, Role = role, RoleId = role.Id, IsActive = true, EmailVerifiedAt = _time.GetUtcNow().UtcDateTime };
            _db.Users.Add(user);
        }
        else if (!user.IsActive) return AppResult<AuthResponse>.Failure(AppErrorType.Unauthorized, "La cuenta no esta activa.");
        else if (linkingUserId is null && !identity.EmailAuthoritative)
            return AppResult<AuthResponse>.Failure(AppErrorType.Conflict, "Ya existe una cuenta con ese email. Inicia sesion con tu password para vincular Google de forma segura.", "google_link_required");
        else if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            return AppResult<AuthResponse>.Failure(AppErrorType.Conflict, "La cuenta de Google debe usar el mismo email que tu cuenta local.", "google_email_mismatch");
        _db.UserExternalLogins.Add(new UserExternalLogin { User = user, Provider = identity.Provider, ProviderSubject = identity.Subject, CreatedAtUtc = _time.GetUtcNow().UtcDateTime });
        await _db.SaveChangesAsync(cancellationToken);
        return AppResult<AuthResponse>.Success(AuthMapping.Auth(user, _jwt));
    }
}

public sealed class LoginUserUseCase : ILoginUserUseCase
{
    private readonly IApplicationDbContext _db; private readonly IPasswordHasher _hasher; private readonly IJwtTokenService _jwt;
    public LoginUserUseCase(IApplicationDbContext db, IPasswordHasher hasher, IJwtTokenService jwt) => (_db, _hasher, _jwt) = (db, hasher, jwt);
    public async Task<AppResult<AuthResponse>> ExecuteAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant(); var user = await _db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Email == email, cancellationToken);
        if (user is null || !user.IsActive || user.EmailVerifiedAt is null || !_hasher.Verify(request.Password, user.PasswordHash)) return AppResult<AuthResponse>.Failure(AppErrorType.Unauthorized, "Credenciales invalidas o email sin verificar.");
        if (_hasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = _hasher.Hash(request.Password);
            await _db.SaveChangesAsync(cancellationToken);
        }
        return AppResult<AuthResponse>.Success(AuthMapping.Auth(user, _jwt));
    }
}

internal static class PasswordReset
{
    public const int LifetimeSeconds = 600;
    public const int MaximumAttempts = 5;
    public const string GenericRequestMessage = "Si el email corresponde a una cuenta, solicitamos el envio de un codigo para restablecer la password.";
    public const string GenericInvalidCodeMessage = "El codigo no es valido, vencio o ya fue utilizado.";
}

public sealed class RequestPasswordResetUseCase : IRequestPasswordResetUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordResetEmailSender _sender;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly ITransactionManager? _transactionManager;

    public RequestPasswordResetUseCase(IApplicationDbContext db, IPasswordResetEmailSender sender, IPasswordHasher hasher, TimeProvider time, ITransactionManager? transactionManager = null) =>
        (_db, _sender, _hasher, _time, _transactionManager) = (db, sender, hasher, time, transactionManager);

    public async Task<AppResult<PasswordResetPendingResponse>> ExecuteAsync(RequestPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var now = _time.GetUtcNow().UtcDateTime;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var user = await _db.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        PasswordResetCode? newCode = null;
        var tokenVersionAtRequest = user?.TokenVersion;

        if (user is not null)
        {
            newCode = new PasswordResetCode
            {
                User = user,
                UserId = user.Id,
                CodeHash = _hasher.Hash(code),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddSeconds(PasswordReset.LifetimeSeconds),
                // A persisted code is not usable until the provider accepts the request.
                ConsumedAtUtc = now
            };
            _db.PasswordResetCodes.Add(newCode);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var send = await _sender.SendAsync(email, code, deliver: user is not null, cancellationToken);
        if (newCode is not null && send.AcceptedByProvider)
        {
            // Do not abandon an accepted provider request because the HTTP client disconnected.
            var activationCancellation = CancellationToken.None;
            await using var transaction = _transactionManager is null
                ? null
                : await _transactionManager.BeginPasswordResetActivationTransactionAsync(user!.Id, activationCancellation);
            var completedAt = _time.GetUtcNow().UtcDateTime;
            var currentTokenVersion = await _db.Users.AsNoTracking()
                .Where(x => x.Id == user!.Id)
                .Select(x => x.TokenVersion)
                .SingleAsync(activationCancellation);
            if (currentTokenVersion != tokenVersionAtRequest)
            {
                // A password change completed while delivery was pending. The request
                // belongs to the previous credential generation and must stay inactive.
                if (transaction is not null) await transaction.CommitAsync(activationCancellation);
                return AppResult<PasswordResetPendingResponse>.Success(new(PasswordReset.GenericRequestMessage, PasswordReset.LifetimeSeconds, send.DevelopmentCode));
            }
            var activeCodes = await _db.PasswordResetCodes
                .Where(x => x.UserId == user!.Id && x.ConsumedAtUtc == null)
                .ToListAsync(activationCancellation);
            foreach (var activeCode in activeCodes)
            {
                activeCode.ConsumedAtUtc = completedAt;
            }
            newCode.ConsumedAtUtc = null;
            await _db.SaveChangesAsync(activationCancellation);
            if (transaction is not null) await transaction.CommitAsync(activationCancellation);
        }
        return AppResult<PasswordResetPendingResponse>.Success(new(PasswordReset.GenericRequestMessage, PasswordReset.LifetimeSeconds, send.DevelopmentCode));
    }
}

public sealed class ConfirmPasswordResetUseCase : IConfirmPasswordResetUseCase
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;
    private readonly ITransactionManager? _transactionManager;

    public ConfirmPasswordResetUseCase(IApplicationDbContext db, IPasswordHasher hasher, TimeProvider time, ITransactionManager? transactionManager = null) =>
        (_db, _hasher, _time, _transactionManager) = (db, hasher, time, transactionManager);

    public async Task<AppResult<PasswordResetCompletedResponse>> ExecuteAsync(ConfirmPasswordResetRequest request, CancellationToken cancellationToken = default)
    {
        if (!new StrongPasswordAttribute().IsValid(request.NewPassword))
            return AppResult<PasswordResetCompletedResponse>.Failure(AppErrorType.Validation, "La password debe tener entre 8 y 128 caracteres e incluir al menos una letra y un numero.");

        var email = request.Email.Trim().ToLowerInvariant();
        var now = _time.GetUtcNow().UtcDateTime;
        var userId = await _db.Users.Where(x => x.Email == email && x.IsActive).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);
        await using var transaction = userId is not null && _transactionManager is not null
            ? await _transactionManager.BeginPasswordResetActivationTransactionAsync(userId.Value, cancellationToken)
            : null;
        var user = await _db.Users.Include(x => x.PasswordResetCodes).SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
        var reset = user?.PasswordResetCodes.Where(x => x.ConsumedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (reset is null || reset.ExpiresAtUtc <= now || reset.FailedAttempts >= PasswordReset.MaximumAttempts)
            return AppResult<PasswordResetCompletedResponse>.Failure(AppErrorType.Validation, PasswordReset.GenericInvalidCodeMessage);

        if (!_hasher.Verify(request.Code, reset.CodeHash))
        {
            reset.FailedAttempts++;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AppResult<PasswordResetCompletedResponse>.Failure(AppErrorType.Validation, PasswordReset.GenericInvalidCodeMessage);
        }

        foreach (var item in user!.PasswordResetCodes.Where(x => x.ConsumedAtUtc == null)) item.ConsumedAtUtc = now;
        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.TokenVersion++;
        user.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return AppResult<PasswordResetCompletedResponse>.Success(new("La password fue actualizada. Ya podes iniciar sesion."));
    }
}

public sealed class GetCurrentUserUseCase : IGetCurrentUserUseCase
{
    private readonly IApplicationDbContext _db; public GetCurrentUserUseCase(IApplicationDbContext db) => _db = db;
    public async Task<AppResult<UserResponse>> ExecuteAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        return user is null ? AppResult<UserResponse>.Failure(AppErrorType.NotFound, "Usuario no encontrado.") : AppResult<UserResponse>.Success(AuthMapping.User(user));
    }
}
