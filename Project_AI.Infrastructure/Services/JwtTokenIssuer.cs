using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class JwtTokenIssuer
{
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly TimeProvider _timeProvider;

    public JwtTokenIssuer(IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider)
    {
        _jwtOptions = jwtOptions;
        _timeProvider = timeProvider;
    }

    public AuthTokens Issue(AuthAccount account, Guid sessionId, string refresh, DateTimeOffset refreshExpiresAt)
    {
        var config = _jwtOptions.Value;
        var now = _timeProvider.GetUtcNow();
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

}
