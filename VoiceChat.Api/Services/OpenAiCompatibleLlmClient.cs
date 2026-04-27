using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceChat.Api.Interfaces;

namespace VoiceChat.Api.Services;

public sealed class OpenAiCompatibleLlmClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<string> StreamChatAsync(
        ProviderConfig config,
        IReadOnlyList<(string Role, string Content)> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (config.RequiresApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
        {
            yield return $"{config.ProviderName} API key is missing. Set {config.SectionName}__ApiKey on the API server.";
            yield break;
        }

        using var request = BuildRequest(config, messages, stream: true);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            yield return await FormatErrorAsync(config.ProviderName, response, cancellationToken);
            yield break;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
                break;

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                continue;

            var data = line["data:".Length..].Trim();
            if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                yield break;

            foreach (var piece in ExtractStreamPieces(data))
                yield return piece;
        }
    }

    public async Task<string?> CompleteChatNonStreamingAsync(
        ProviderConfig config,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default)
    {
        if (config.RequiresApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
            return $"{config.ProviderName} API key is missing. Set {config.SectionName}__ApiKey on the API server.";

        using var request = BuildRequest(config, messages, stream: false);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return await FormatErrorAsync(config.ProviderName, response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ExtractCompleteText(json);
    }

    private static HttpRequestMessage BuildRequest(
        ProviderConfig config,
        IReadOnlyList<(string Role, string Content)> messages,
        bool stream)
    {
        var baseUrl = NormalizeBaseUrl(config.BaseUrl);
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "chat/completions"));
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());
        request.Content = JsonContent.Create(
            new ChatCompletionRequest(
                config.Model,
                BuildMessages(messages),
                stream,
                config.Temperature),
            options: JsonOptions);
        return request;
    }

    private static List<ChatMessage> BuildMessages(IReadOnlyList<(string Role, string Content)> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var (role, content) in messages)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var normalizedRole = role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? "assistant"
                : role.Equals("system", StringComparison.OrdinalIgnoreCase)
                    ? "system"
                    : "user";
            result.Add(new ChatMessage(normalizedRole, content));
        }

        if (result.Count == 0)
            result.Add(new ChatMessage("user", "Hello"));

        return result;
    }

    private static Uri NormalizeBaseUrl(string baseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.openai.com/v1/" : baseUrl.Trim();
        if (!raw.EndsWith('/'))
            raw += "/";
        return new Uri(raw);
    }

    private static IEnumerable<string> ExtractStreamPieces(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices))
            yield break;

        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
    }

    private static string? ExtractCompleteText(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices))
            return null;

        var builder = new StringBuilder();
        foreach (var choice in choices.EnumerateArray())
        {
            if (!choice.TryGetProperty("message", out var message))
                continue;

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                builder.Append(content.GetString());
        }

        var text = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static async Task<string> FormatErrorAsync(
        string provider,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => $"{provider} rejected the API key. Verify the server environment variable.",
            HttpStatusCode.TooManyRequests => $"{provider} rate limit or quota was reached. Check billing/credits and try again later.",
            _ => $"{provider} request failed with HTTP {(int)response.StatusCode}: {body}"
        };
    }

    private sealed record ChatCompletionRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        bool Stream,
        double? Temperature);

    private sealed record ChatMessage(string Role, string Content);

    public sealed record ProviderConfig(
        string ProviderName,
        string SectionName,
        string ApiKey,
        string BaseUrl,
        string Model,
        double? Temperature,
        bool RequiresApiKey = true);
}
