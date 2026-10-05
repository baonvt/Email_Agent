using Project_AI.Application.DTOs.Mailboxes;

namespace Project_AI.Application.Interfaces;

public interface IGoogleOAuthClient
{
    string CreateAuthorizationUrl(string state, string codeChallenge);
    Task<GoogleTokenSet> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);
    Task<GoogleMailboxAccount> GetMailboxAccountAsync(string accessToken, CancellationToken cancellationToken);
    Task<GoogleTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}
