using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinansApi.Data;
using Microsoft.IdentityModel.Tokens;

namespace FinansApi.Auth;

public class TokenService(JwtSettings settings)
{
    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(settings.ExpiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email)

        };
        var token = new JwtSecurityToken(issuer: settings.Issuer, audience: settings.Audience, claims: claims, expires: expiresAt, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

}

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : throw new FinansApi.Infrastructure.Errors.DomainException(401, "Kullanıcı bilgisi okunamadı.");
    }

}
