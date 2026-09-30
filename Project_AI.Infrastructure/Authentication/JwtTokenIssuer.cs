using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;


namespace Project_AI.Infrastructure.Authentication;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    public AuthTokens Issue(AuthAccount account, Guid sessionId, string refresh, DateTimeOffset refreshExpiresAt)
    {
        var config = options.Value;
        var now = clock.GetUtcNow();
        var expiresAt = now.AddMinutes(config.AccessTokenMinutes);
        if (expiresAt > refreshExpiresAt) expiresAt = refreshExpiresAt;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Profile.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, account.Profile.Email),
            new("sid", sessionId.ToString()),
            new("ss", account.SecurityStamp)
        };
        claims.AddRange(account.Profile.Roles.Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(config.Issuer, config.Audience, claims,
            now.UtcDateTime, expiresAt.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(config.SigningKey)),
                SecurityAlgorithms.HmacSha256));
        return new AuthTokens(new JwtSecurityTokenHandler().WriteToken(token), expiresAt,
            refresh, refreshExpiresAt, account.Profile);
    }

    public static string NewRefreshToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders
        .Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
    public static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
