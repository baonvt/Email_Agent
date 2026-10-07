using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Project_AI.Application.Common.Enums;
using Project_AI.Application.Common.Exceptions;
using Project_AI.Infrastructure.Options;

namespace Project_AI.Infrastructure.Services;

public sealed class GeminiJsonClient
{
    private readonly HttpClient _httpClient;
    private readonly GeminiOptions _options;
    public GeminiJsonClient(HttpClient httpClient, IOptions<GeminiOptions> options)
    {
        _httpClient = httpClient; _options = options.Value;
    }
    public string Model => _options.Model;
    public int MaxInputCharacters => _options.MaxInputCharacters;
    public async Task<string> GenerateAsync(object payload, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.HasValidCredentials())
            throw new AppException(ErrorCode.AiNotConfigured, "Configure Gemini before processing emails.");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent");
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(payload);
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AppException(ErrorCode.AiThrottled, "Gemini is rate limited.");
            if (!response.IsSuccessStatusCode) throw Unavailable();
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
            var root = document.RootElement;
            if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out _))
                throw InvalidResponse();
            var candidates = root.GetProperty("candidates");
            if (candidates.GetArrayLength() != 1 || candidates[0].GetProperty("finishReason").GetString() != "STOP")
                throw InvalidResponse();
            var text = new StringBuilder();
            foreach (var part in candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray())
            {
                if (part.EnumerateObject().Any(p => p.Name is not ("text" or "thought"))) throw InvalidResponse();
                if (part.TryGetProperty("thought", out var thought) && thought.GetBoolean()) continue;
                text.Append(part.GetProperty("text").GetString());
            }
            return text.ToString();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw InvalidResponse();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw Unavailable(); }
    }
    private static AppException Unavailable() => new(ErrorCode.AiUnavailable, "Gemini is temporarily unavailable.");
    private static AppException InvalidResponse() => new(ErrorCode.AiInvalidResponse, "Gemini returned invalid email content.");
}
