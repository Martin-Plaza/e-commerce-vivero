using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GymShop.Api.Security;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using GymShop.Infrastructure.Services;
using GymShop.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GymShop.Tests.Api;

public sealed class JwtSessionValidationTests
{
    private const string Issuer = "GymShop.Tests";
    private const string Audience = "GymShop.Tests.Client";
    private const string Secret = "test-signing-secret-with-at-least-32-characters";

    [Fact]
    public async Task Active_user_with_current_version_authenticates()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "User");

        var result = await AuthenticateAsync(db, CreateToken(user));

        Assert.True(result.AuthenticateResult.Succeeded);
    }

    [Fact]
    public async Task Deactivated_user_receives_401()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "User");
        var token = CreateToken(user);
        user.IsActive = false;
        user.TokenVersion++;
        await db.SaveChangesAsync();

        var result = await AuthenticateAsync(db, token);

        Assert.False(result.AuthenticateResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Role_change_invalidates_previous_token_with_401()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "Admin");
        var token = CreateToken(user);
        user.RoleId = db.Roles.Single(x => x.Name == "User").Id;
        user.TokenVersion++;
        await db.SaveChangesAsync();

        var result = await AuthenticateAsync(db, token);

        Assert.False(result.AuthenticateResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Wrong_token_version_receives_401()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "User");
        var token = CreateToken(user, tokenVersion: user.TokenVersion + 1);

        var result = await AuthenticateAsync(db, token);

        Assert.False(result.AuthenticateResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Missing_user_receives_401()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var role = db.Roles.Single(x => x.Name == "User");
        var missingUser = new User { Id = 999999, Email = "missing@test.com", Name = "Missing", Role = role, RoleId = role.Id };

        var result = await AuthenticateAsync(db, CreateToken(missingUser));

        Assert.False(result.AuthenticateResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task Legacy_token_without_version_receives_401()
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, "User");
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.Name)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        var result = await AuthenticateAsync(db, new JwtSecurityTokenHandler().WriteToken(token));

        Assert.False(result.AuthenticateResult.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }

    [Theory]
    [InlineData("User", "Admin")]
    [InlineData("Admin", "SuperAdmin")]
    public async Task Valid_token_with_insufficient_role_is_forbidden(string currentRole, string requiredRole)
    {
        await using var db = await TestDbContextFactory.CreateAsync();
        var user = await SeedUserAsync(db, currentRole);
        var result = await AuthenticateAsync(db, CreateToken(user));
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddAuthorizationCore();
        using var services = serviceCollection.BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();

        var authorized = await authorization.AuthorizeAsync(
            result.AuthenticateResult.Principal!,
            resource: null,
            new AuthorizationPolicyBuilder().RequireRole(requiredRole).Build());

        Assert.True(result.AuthenticateResult.Succeeded);
        Assert.False(authorized.Succeeded);
    }

    private static async Task<(AuthenticateResult AuthenticateResult, int StatusCode)> AuthenticateAsync(
        GymShop.Infrastructure.Data.GymShopDbContext db,
        string token)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddScoped<JwtTokenValidationEvents>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.EventsType = typeof(JwtTokenValidationEvents);
                options.TokenValidationParameters = TokenValidationParameters();
            });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var authenticateResult = await context.AuthenticateAsync();
        if (!authenticateResult.Succeeded)
        {
            await context.ChallengeAsync();
        }

        return (authenticateResult, context.Response.StatusCode);
    }

    private static string CreateToken(User user, int? tokenVersion = null)
    {
        var tokenUser = new User
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            Role = user.Role,
            RoleId = user.RoleId,
            TokenVersion = tokenVersion ?? user.TokenVersion,
            MfaEnabledAtUtc = user.MfaEnabledAtUtc
        };
        return new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            Secret = Secret,
            ExpirationMinutes = 5
        })).CreateToken(tokenUser);
    }

    private static TokenValidationParameters TokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };

    private static async Task<User> SeedUserAsync(GymShop.Infrastructure.Data.GymShopDbContext db, string roleName)
    {
        var role = db.Roles.Single(x => x.Name == roleName);
        var user = new User
        {
            Email = $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@test.com",
            Name = "Session Test",
            PasswordHash = "not-used",
            RoleId = role.Id,
            Role = role,
            IsActive = true,
            MfaEnabledAtUtc = roleName is "Admin" or "SuperAdmin" ? DateTime.UtcNow : null
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
