using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace FuelGuard.Api.Security;

public sealed class AccessTokenIssuer(IConfiguration configuration)
{
    public (string Token, DateTimeOffset ExpiresAtUtc) Create(IEnumerable<Claim> claims)
    {
        var key = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configure Jwt:SigningKey with at least 32 random bytes using .NET User Secrets or environment variables.");
        var expires = DateTimeOffset.UtcNow.AddMinutes(15);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = configuration["Jwt:Issuer"] ?? "ROMVEX.FuelGuard",
            Audience = configuration["Jwt:Audience"] ?? "ROMVEX.FuelGuard.Clients",
            NotBefore = DateTime.UtcNow,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        };
        var handler = new JwtSecurityTokenHandler();
        return (handler.WriteToken(handler.CreateToken(descriptor)), expires);
    }
}
