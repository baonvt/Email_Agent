using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Application.DTOs.Analysis;
using Project_AI.Application.Interfaces;
using Project_AI.Domain.Enums;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class GeminiEmailAnalyzer : IEmailAnalyzer
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;

    public GeminiEmailAnalyzer(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<EmailAnalysisOutput> AnalyzeAsync(EmailAnalysisInput email, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.HasValidCredentials())
            throw new AppException(ErrorCode.AiNotConfigured, "Configure Gemini before analyzing emails.");
        var (payload, truncated) = GeminiAnalysisPrompt.Create(email, _options.MaxInputCharacters);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent");
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AppException(ErrorCode.AiThrottled, "Gemini is rate limited. Try again later.");
            if (!response.IsSuccessStatusCode)
                throw new AppException(ErrorCode.AiUnavailable, "Gemini is unavailable. Check its configuration or try later.");
            // Bound streamed responses too; Content-Length and HttpClient's buffer limit alone are insufficient.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(bytes, cancellationToken)) > 0)
            {
                if (buffer.Length + read > 256 * 1024) throw InvalidResponse();
                buffer.Write(bytes, 0, read);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            return Parse(document.RootElement, truncated);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw InvalidResponse();
        }
        catch (HttpRequestException)
        {
            throw new AppException(ErrorCode.AiUnavailable, "Gemini could not be reached.");
        }
        catch (IOException)
        {
            throw new AppException(ErrorCode.AiUnavailable, "Gemini could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AppException(ErrorCode.AiUnavailable, "Gemini timed out. Try again later.");
        }
    }

    private EmailAnalysisOutput Parse(JsonElement root, bool truncated)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
            throw InvalidResponse();
        var candidates = root.GetProperty("candidates");
        if (candidates.GetArrayLength() != 1) throw InvalidResponse();
        var candidate = candidates[0];
        if (candidate.GetProperty("finishReason").GetString() != "STOP") throw InvalidResponse();
        var text = new StringBuilder();
        foreach (var part in candidate.GetProperty("content").GetProperty("parts").EnumerateArray())
        {
            if (part.EnumerateObject().Any(p => p.Name is not ("text" or "thought"))) throw InvalidResponse();
            if (part.TryGetProperty("thought", out var thought) && thought.GetBoolean()) continue;
            text.Append(part.GetProperty("text").GetString());
        }
        using var result = JsonDocument.Parse(text.ToString());
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
        return new EmailAnalysisOutput(summary.Trim(), category, priority, confidence, truncated, "gemini", _options.Model);
    }

    private static AppException InvalidResponse() => new(ErrorCode.AiInvalidResponse, "Gemini returned an invalid analysis.");
}
