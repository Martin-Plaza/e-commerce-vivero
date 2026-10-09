using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GymShop.Application.Abstractions;
using GymShop.Domain.Entities;
using GymShop.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GymShop.Infrastructure.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string CreateToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.Name),
            new(JwtClaimNames.TokenVersion, user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (user.Role.Name is "Admin" or "SuperAdmin" && user.MfaEnabledAtUtc is not null)
            claims.Add(new Claim(JwtClaimNames.Mfa, "true"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
