using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Auth;
using Project_AI.Application.Interfaces;
using Project_AI.Infrastructure.Options;
using Project_AI.Infrastructure.Repositories;

namespace Project_AI.Infrastructure.Services;

public sealed class AuthSessionService : IAuthSessionService
{
    private readonly AuthSessionRepository _sessionRepository;
    private readonly JwtTokenIssuer _tokenIssuer;
    private readonly TokenBlacklist _tokenBlacklist;
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly TimeProvider _timeProvider;

    public AuthSessionService(
        AuthSessionRepository sessionRepository,
        JwtTokenIssuer tokenIssuer,
        TokenBlacklist tokenBlacklist,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider timeProvider)
    {
        _sessionRepository = sessionRepository;
        _tokenIssuer = tokenIssuer;
        _tokenBlacklist = tokenBlacklist;
        _jwtOptions = jwtOptions;
        _timeProvider = timeProvider;
    }

    public async Task<AuthTokens> CreateAsync(AuthAccount account, CancellationToken cancellationToken)
    {
        await _tokenBlacklist.ContainsAsync("availability-check");
        var refresh = NewRefreshToken();
        var data = await _sessionRepository.CreateAsync(account, HashRefreshToken(refresh),
            _timeProvider.GetUtcNow().AddDays(_jwtOptions.Value.RefreshTokenDays), cancellationToken);
        return _tokenIssuer.Issue(data.Account, data.SessionId, refresh, data.ExpiresAt);
    }

    public async Task<AuthTokens> RefreshAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        {
            throw new AppException(ErrorCode.InvalidSession, "Please sign in again.");
        }
        await _tokenBlacklist.ContainsAsync("availability-check");
        var refresh = NewRefreshToken();
        var data = await _sessionRepository.RotateAsync(HashRefreshToken(token), HashRefreshToken(refresh), cancellationToken);
        return _tokenIssuer.Issue(data.Account, data.SessionId, refresh, data.ExpiresAt);
    }

    public async Task RevokeAsync(AccessTokenContext context, bool allSessions, CancellationToken cancellationToken)
    {
        // Keep database revocation committed even if writing the Redis blacklist subsequently fails.
        await _sessionRepository.RevokeAsync(context, allSessions, cancellationToken);
        await _tokenBlacklist.AddAsync(context.Jti, context.ExpiresAt);
    }

    public async Task<bool> ValidateAsync(AccessTokenContext context, CancellationToken cancellationToken) =>
        context.ExpiresAt > _timeProvider.GetUtcNow() && !await _tokenBlacklist.ContainsAsync(context.Jti)
        && await _sessionRepository.IsActiveAsync(context, cancellationToken);

    private static string NewRefreshToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
