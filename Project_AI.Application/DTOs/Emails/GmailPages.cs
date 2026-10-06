namespace Project_AI.Application.DTOs.Emails;

public sealed record GmailMessagePage(IReadOnlyList<string> MessageIds, string? NextPageToken);
public sealed record GmailHistoryPage(IReadOnlyList<string> ChangedMessageIds, string HistoryId, string? NextPageToken);
