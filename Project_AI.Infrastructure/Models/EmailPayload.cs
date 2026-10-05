
namespace Project_AI.Infrastructure.Models;

public sealed record EmailPayload(string To, string Subject, string Html, string Text);
