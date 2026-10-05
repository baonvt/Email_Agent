using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class AuthSessionService(IAuthSessionRepository repository, JwtTokenIssuer issuer,
    TokenBlacklist blacklist, IOptions<JwtOptions> options, TimeProvider clock) : IAuthSessions
{
    public async Task<AuthTokens> CreateAsync(AuthAccount account, CancellationToken ct)
    {
        await blacklist.ContainsAsync("availability-check");
        var refresh = JwtTokenIssuer.NewRefreshToken();
        var data = await repository.CreateAsync(account, JwtTokenIssuer.HashRefreshToken(refresh),
            clock.GetUtcNow().AddDays(options.Value.RefreshTokenDays), ct);
        return issuer.Issue(data.Account, data.SessionId, refresh, data.ExpiresAt);
    }

    public async Task<AuthTokens> RefreshAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            throw new AppException(ErrorCode.InvalidSession, "Please sign in again.");
        await blacklist.ContainsAsync("availability-check");
        var refresh = JwtTokenIssuer.NewRefreshToken();
        var data = await repository.RotateAsync(JwtTokenIssuer.HashRefreshToken(token),
            JwtTokenIssuer.HashRefreshToken(refresh), ct);
        return issuer.Issue(data.Account, data.SessionId, refresh, data.ExpiresAt);
    }

    public async Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken ct)
    {
        // Keep database revocation committed even if writing the Redis blacklist subsequently fails.
        await repository.RevokeAsync(context, allSessions, ct);
        await blacklist.AddAsync(context.Jti, context.ExpiresAt);
    }

    public async Task<bool> ValidateAsync(AccessTokenContext context, CancellationToken ct) =>
        context.ExpiresAt > clock.GetUtcNow() && !await blacklist.ContainsAsync(context.Jti)
        && await repository.IsActiveAsync(context, ct);
}
