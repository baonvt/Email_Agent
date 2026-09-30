namespace Project_AI.Application.Abstractions.Auth;

public interface IAuthEmailSender
{
    Task QueueConfirmationAsync(AuthAccount account, string token, CancellationToken ct);
    Task QueuePasswordResetAsync(AuthAccount account, string token, CancellationToken ct);
}
