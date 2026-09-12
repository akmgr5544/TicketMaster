using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Users.Api.Entities;
using Users.Api.Options;
using System.Security.Cryptography;

namespace Users.Api.Features.Users;

public static class TokenService
{
    public static string CreateToken(User user, AuthOptions authOptions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.Token));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: authOptions.Issuer,
            audience: authOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
    }

    // Raw is handed to the client; only Hash is persisted (a leaked table of plaintext refresh
    // tokens is a leaked table of live sessions). Expires is set here from AuthOptions so the
    // lifetime lives in one place rather than a hidden setter side effect.
    public sealed record RefreshTokenResult(string Raw, string Hash, DateTime Expires);

    public static RefreshTokenResult CreateRefreshToken(AuthOptions authOptions)
    {
        var randomNumber = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        var raw = Convert.ToBase64String(randomNumber);

        return new RefreshTokenResult(
            raw,
            HashRefreshToken(raw),
            DateTime.UtcNow.AddDays(authOptions.RefreshTokenDays));
    }

    // SHA-256 hex. A 64-char result fits the existing varchar(120) RefreshToken column. Both storage
    // and lookup go through this method so the two always agree on casing.
    public static string HashRefreshToken(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}