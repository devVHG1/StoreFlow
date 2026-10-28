using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StoreFlow.Application.DTOs;
using StoreFlow.Application.Interfaces;
using StoreFlow.Domain.Entities;

namespace StoreFlow.Аpi.Security;

public class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    public AuthDto Create(User user)
    {
        var settings = options.Value;
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim("role", user.Role), new Claim("ver", user.TokenVersion.ToString())
        };
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires,
            new UserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Role, user.CreatedAt));
    }
}
