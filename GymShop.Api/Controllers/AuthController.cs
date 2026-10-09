using GymShop.Application.Abstractions;
using GymShop.Application.Common;
using GymShop.Application.DTOs.Auth;
using GymShop.Application.UseCases.Auth;
using GymShop.Api.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using GymShop.Api.Security;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GymShop.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly IRegisterUserUseCase _registerUser;
    private readonly ILoginUserUseCase _loginUser;
    private readonly IVerifyEmailUseCase _verifyEmail;
    private readonly IResendVerificationUseCase _resendVerification;
    private readonly IGoogleLoginUseCase _googleLogin;
    private readonly IRequestPasswordResetUseCase _requestPasswordReset;
    private readonly IConfirmPasswordResetUseCase _confirmPasswordReset;
    private readonly IGetCurrentUserUseCase _getCurrentUser;
    private readonly IGymShopRequestLimiter _requestLimiter;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IJwtTokenService _jwtTokens;
    private readonly IMfaService _mfa;
    private readonly JwtOptions _jwtOptions;
    private readonly MfaOptions _mfaOptions;
    private readonly IHostEnvironment _environment;

    public AuthController(
        IRegisterUserUseCase registerUser,
        ILoginUserUseCase loginUser,
        IVerifyEmailUseCase verifyEmail,
        IResendVerificationUseCase resendVerification,
        IGoogleLoginUseCase googleLogin,
        IRequestPasswordResetUseCase requestPasswordReset,
        IConfirmPasswordResetUseCase confirmPasswordReset,
        IGetCurrentUserUseCase getCurrentUser,
        IGymShopRequestLimiter requestLimiter,
        IRefreshTokenService refreshTokens,
        IJwtTokenService jwtTokens,
        IOptions<JwtOptions> jwtOptions,
        IMfaService mfa,
        IOptions<MfaOptions> mfaOptions,
        IHostEnvironment environment)
    {
        _registerUser = registerUser;
        _loginUser = loginUser;
        _verifyEmail = verifyEmail;
        _resendVerification = resendVerification;
        _googleLogin = googleLogin;
        _requestPasswordReset = requestPasswordReset;
        _confirmPasswordReset = confirmPasswordReset;
        _getCurrentUser = getCurrentUser;
        _requestLimiter = requestLimiter;
        _refreshTokens = refreshTokens;
        _jwtTokens = jwtTokens;
        _mfa = mfa;
        _jwtOptions = jwtOptions.Value;
        _mfaOptions = mfaOptions.Value;
        _environment = environment;
    }

    /*
        * EnableRateLimiting es un atributo de .net para aplicar politicas, en este caso por IP, que esta declarado en program.cs
        * producesResponseType describe el contrato del endpoint, es decir, que respuestas podria devolver.
        * el DTO RegistrationPendingResponse devolverá (string Email, int ExpiresInSeconds, string? DevelopmentCode)
        * la funcion register espera registerRequest (name, lastname, email y password)
        * decision le pasa la politica a acquire, y valida si puede seguir, en el caso de que no, devuelve el ratelimitResponse (declarado en gymshopRequestLimiter.cs)
        * si pasa va paea AuthUseCases.cs

     */
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.RegistrationIp)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegistrationPendingResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var decision = _requestLimiter.Acquire(RateLimitPolicies.RegistrationGlobal, "all");
        if (!decision.IsAllowed) return RateLimitResponse.Create(HttpContext, decision);
        return FromResult(await _registerUser.ExecuteAsync(request, cancellationToken));
    }



    [HttpPost("verify-email")]
    [EnableRateLimiting(RateLimitPolicies.RegistrationIp)]
    public async Task<ActionResult<AuthResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        await CompleteAuthenticationAsync(await _verifyEmail.ExecuteAsync(request, cancellationToken), cancellationToken);



    [HttpPost("resend-verification")]
    [EnableRateLimiting(RateLimitPolicies.RegistrationIp)]
    public async Task<ActionResult<RegistrationPendingResponse>> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken) =>
        FromResult(await _resendVerification.ExecuteAsync(request, cancellationToken));



    [HttpPost("google")]
    [EnableRateLimiting(RateLimitPolicies.LoginIp)]
    public async Task<ActionResult<AuthResponse>> Google(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var linkingUserId = int.TryParse(subject, out var userId) ? userId : (int?)null;
        return await CompleteAuthenticationAsync(await _googleLogin.ExecuteAsync(request, linkingUserId, cancellationToken), cancellationToken);
    }



    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.LoginIp)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var accountKey = GymShopRequestLimiter.HashAccount(request.Email);
        var decision = _requestLimiter.Acquire(RateLimitPolicies.LoginAccount, accountKey);
        if (!decision.IsAllowed) return RateLimitResponse.Create(HttpContext, decision);
        return await CompleteAuthenticationAsync(await _loginUser.ExecuteAsync(request, cancellationToken), cancellationToken);
    }



    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.PasswordResetIp)]
    [ProducesResponseType(typeof(PasswordResetPendingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PasswordResetPendingResponse>> ForgotPassword(RequestPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var decision = _requestLimiter.Acquire(RateLimitPolicies.PasswordResetAccount, GymShopRequestLimiter.HashAccount(request.Email));
        if (!decision.IsAllowed) return RateLimitResponse.Create(HttpContext, decision);
        return FromResult(await _requestPasswordReset.ExecuteAsync(request, cancellationToken));
    }



    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.PasswordResetIp)]
    [ProducesResponseType(typeof(PasswordResetCompletedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PasswordResetCompletedResponse>> ResetPassword(ConfirmPasswordResetRequest request, CancellationToken cancellationToken)
    {
        var decision = _requestLimiter.Acquire(RateLimitPolicies.PasswordResetAccount, GymShopRequestLimiter.HashAccount(request.Email));
        if (!decision.IsAllowed) return RateLimitResponse.Create(HttpContext, decision);
        return FromResult(await _confirmPasswordReset.ExecuteAsync(request, cancellationToken));
    }


    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(
        [FromServices] ICurrentUserService currentUser,
        CancellationToken cancellationToken)
    {
        return FromResult(await _getCurrentUser.ExecuteAsync(currentUser.UserId, cancellationToken));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(BrowserSessionSecurity.RefreshCookie, out var refreshToken))
            return Unauthorized(new { message = "La sesión ya no es válida." });

        var rotation = await _refreshTokens.RotateAsync(refreshToken, cancellationToken);
        if (rotation is null)
        {
            DeleteSessionCookies();
            return Unauthorized(new { message = "La sesión ya no es válida." });
        }

        var response = new AuthResponse(
            _jwtTokens.CreateToken(rotation.User),
            new UserResponse(rotation.User.Id, rotation.User.Email, rotation.User.Name, rotation.User.LastName, rotation.User.Role.Name));
        WriteSessionCookies(response.Token, rotation.RefreshToken);
        return Ok(response);
    }

    [HttpGet("csrf")]
    public ActionResult<object> Csrf()
    {
        var token = BrowserSessionSecurity.CreateCsrfToken();
        WriteCsrfCookie(token);
        return Ok(new { token });
    }

    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        Request.Cookies.TryGetValue(BrowserSessionSecurity.RefreshCookie, out var refreshToken);
        await _refreshTokens.RevokeAsync(refreshToken, cancellationToken);
        DeleteSessionCookies();
        return NoContent();
    }

    [HttpPost("mfa/setup")]
    [EnableRateLimiting(RateLimitPolicies.LoginIp)]
    public async Task<ActionResult<MfaSetupResponse>> BeginMfaSetup(CancellationToken cancellationToken)
    {
        if (!TryGetMfaChallenge(out var challenge)) return Unauthorized();
        var setup = await _mfa.BeginSetupAsync(challenge, cancellationToken);
        return setup is null ? Unauthorized() : Ok(new MfaSetupResponse(setup.SharedKey, setup.OtpAuthUri, setup.QrCodeRows));
    }

    [HttpPost("mfa/enable")]
    [EnableRateLimiting(RateLimitPolicies.LoginIp)]
    public async Task<ActionResult<MfaCompletedResponse>> EnableMfa(MfaCodeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetMfaChallenge(out var challenge)) return Unauthorized();
        var verified = await _mfa.EnableAsync(challenge, request.Code, cancellationToken);
        return verified is null ? BadRequest(new { message = "El código de autenticación no es válido." }) : await CompleteMfaAsync(verified, cancellationToken);
    }

    [HttpPost("mfa/complete")]
    [EnableRateLimiting(RateLimitPolicies.LoginIp)]
    public async Task<ActionResult<MfaCompletedResponse>> CompleteMfa(MfaCodeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetMfaChallenge(out var challenge)) return Unauthorized();
        var verified = await _mfa.CompleteAsync(challenge, request.Code, cancellationToken);
        return verified is null ? BadRequest(new { message = "El código de autenticación no es válido." }) : await CompleteMfaAsync(verified, cancellationToken);
    }

    private async Task<ActionResult<AuthResponse>> CompleteAuthenticationAsync(
        AppResult<AuthResponse> result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess) return ToErrorResponse(result.Error!);
        var requirement = await _mfa.CreateLoginRequirementAsync(result.Value!.User.Id, cancellationToken);
        if (requirement is not null)
        {
            WriteMfaChallengeCookie(requirement.ChallengeToken);
            WriteCsrfCookie(BrowserSessionSecurity.CreateCsrfToken());
            return Ok(new MfaChallengeResponse(true, requirement.SetupRequired, result.Value.User));
        }
        var refreshToken = await _refreshTokens.IssueAsync(result.Value!.User.Id, cancellationToken);
        WriteSessionCookies(result.Value.Token, refreshToken);
        return Ok(result.Value);
    }

    private async Task<ActionResult<MfaCompletedResponse>> CompleteMfaAsync(MfaVerification verified, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokens.CreateToken(verified.User);
        var refreshToken = await _refreshTokens.IssueAsync(verified.User.Id, cancellationToken);
        WriteSessionCookies(accessToken, refreshToken);
        Response.Cookies.Delete(BrowserSessionSecurity.MfaChallengeCookie, new CookieOptions { Path = "/api/auth/mfa" });
        return Ok(new MfaCompletedResponse(new UserResponse(verified.User.Id, verified.User.Email, verified.User.Name, verified.User.LastName, verified.User.Role.Name), verified.RecoveryCodes));
    }

    private bool TryGetMfaChallenge(out string challenge) =>
        Request.Cookies.TryGetValue(BrowserSessionSecurity.MfaChallengeCookie, out challenge!) && !string.IsNullOrWhiteSpace(challenge);

    private void WriteMfaChallengeCookie(string challenge)
    {
        Response.Cookies.Append(
            BrowserSessionSecurity.MfaChallengeCookie,
            challenge,
            BrowserCookieOptions(true, "/api/auth/mfa", TimeSpan.FromMinutes(_mfaOptions.ChallengeMinutes)));
    }

    private void WriteSessionCookies(string accessToken, string refreshToken)
    {
        Response.Cookies.Append(
            BrowserSessionSecurity.AccessCookie,
            accessToken,
            BrowserCookieOptions(true, "/", TimeSpan.FromMinutes(_jwtOptions.ExpirationMinutes)));
        Response.Cookies.Append(
            BrowserSessionSecurity.RefreshCookie,
            refreshToken,
            BrowserCookieOptions(true, "/api/auth", TimeSpan.FromDays(_jwtOptions.RefreshExpirationDays)));
        WriteCsrfCookie(BrowserSessionSecurity.CreateCsrfToken());
    }

    private void WriteCsrfCookie(string token)
    {
        Response.Cookies.Append(
            BrowserSessionSecurity.CsrfCookie,
            token,
            BrowserCookieOptions(false, "/", TimeSpan.FromDays(_jwtOptions.RefreshExpirationDays)));
        Response.Headers[BrowserSessionSecurity.CsrfHeader] = token;
    }

    private void DeleteSessionCookies()
    {
        Response.Cookies.Delete(BrowserSessionSecurity.AccessCookie, BrowserCookieOptions(true, "/"));
        Response.Cookies.Delete(BrowserSessionSecurity.RefreshCookie, BrowserCookieOptions(true, "/api/auth"));
        Response.Cookies.Delete(BrowserSessionSecurity.CsrfCookie, BrowserCookieOptions(false, "/"));
        Response.Cookies.Delete(BrowserSessionSecurity.MfaChallengeCookie, BrowserCookieOptions(true, "/api/auth/mfa"));
    }

    private CookieOptions BrowserCookieOptions(bool httpOnly, string path, TimeSpan? maxAge = null)
    {
        var secure = !_environment.IsDevelopment();
        var options = new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = secure,
            SameSite = secure ? SameSiteMode.None : SameSiteMode.Lax,
            Path = path,
            MaxAge = maxAge
        };
        if (secure) options.Extensions.Add("Partitioned");
        return options;
    }
}
