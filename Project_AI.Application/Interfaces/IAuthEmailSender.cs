using Project_AI.Application.DTOs.Auth;

namespace Project_AI.Application.Interfaces;

public interface IAuthEmailSender
{
    Task QueueConfirmationAsync(AuthAccount account, string token, CancellationToken cancellationToken);
    Task QueuePasswordResetAsync(AuthAccount account, string token, CancellationToken cancellationToken);
}
