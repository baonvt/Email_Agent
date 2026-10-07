using System.Text.Json;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Analysis;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Enums;

namespace Project_AI.Infrastructure.Services;

public sealed class GeminiEmailAnalyzer : IEmailAnalyzer
{
    private readonly GeminiJsonClient _client;
    public GeminiEmailAnalyzer(GeminiJsonClient client) { _client = client; }
    public async Task<EmailAnalysisOutput> AnalyzeAsync(EmailAnalysisInput email, CancellationToken cancellationToken)
    {
        var (payload, truncated) = GeminiAnalysisPrompt.Create(email, _client.MaxInputCharacters);
        var text = await _client.GenerateAsync(payload, cancellationToken);
        try
        {
            using var result = JsonDocument.Parse(text);
            var value = result.RootElement;
            var names = value.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Length != 4 || names.Distinct(StringComparer.Ordinal).Count() != 4
                || names.Except(new[] { "summary", "category", "priority", "confidence" }).Any()) throw InvalidResponse();
            var summary = value.GetProperty("summary").GetString();
            var confidence = value.GetProperty("confidence").GetDouble();
            var category = value.GetProperty("category").GetString() switch
            {
                "work" => EmailCategory.Work, "finance" => EmailCategory.Finance, "personal" => EmailCategory.Personal,
                "promotion" => EmailCategory.Promotion, "spam" => EmailCategory.Spam, "other" => EmailCategory.Other,
                _ => throw InvalidResponse()
            };
            var priority = value.GetProperty("priority").GetString() switch
            {
                "low" => EmailPriority.Low, "normal" => EmailPriority.Normal, "high" => EmailPriority.High,
                _ => throw InvalidResponse()
            };
            if (string.IsNullOrWhiteSpace(summary) || summary.Length > 2000 || summary.Contains('\0')
                || !double.IsFinite(confidence) || confidence is < 0 or > 1) throw InvalidResponse();
            return new EmailAnalysisOutput(summary.Trim(), category, priority, confidence, truncated, "gemini", _client.Model);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw InvalidResponse();
        }
    }
    private static AppException InvalidResponse() => new(ErrorCode.AiInvalidResponse, "Gemini returned an invalid analysis.");
}
