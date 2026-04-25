using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

/// <summary>
/// Streams chat from the Google Gemini Developer API.
/// </summary>
public sealed class GeminiLlmClient(HttpClient http, IOptions<GeminiOptions> options) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            yield return LlmFallbackMessages.GeminiApiKeyMissing();
            yield break;
        }

        var resolved = ResolveModel(model);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildModelPath(resolved, stream: true, opts.ApiKey))
        {
            Content = JsonContent.Create(BuildRequest(messages, opts), options: JsonOptions)
        };

        HttpResponseMessage? response;
        string? connectionError = null;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            connectionError = LlmFallbackMessages.GeminiUnavailable(ex.Message);
            response = null;
        }

        if (connectionError is not null)
        {
            yield return connectionError;
            yield break;
        }

        ArgumentNullException.ThrowIfNull(response);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return FormatGeminiHttpError(response.StatusCode, body);
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                var json = ExtractSseJson(line);
                if (json is null)
                    continue;

                foreach (var piece in ExtractTextPieces(json))
                    yield return piece;
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> CompleteChatNonStreamingAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            return null;

        var resolved = ResolveModel(model);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildModelPath(resolved, stream: false, opts.ApiKey))
        {
            Content = JsonContent.Create(BuildRequest(messages, opts), options: JsonOptions)
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var text = ExtractText(json).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    private string ResolveModel(string requested)
    {
        var fallback = LlmRuntime.DefaultChatModel(options.Value);
        return LlmRuntime.NormalizeChatModel(requested, options.Value) ?? fallback;
    }

    private static string BuildModelPath(string model, bool stream, string apiKey)
    {
        var cleanModel = model.Trim();
        if (cleanModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            cleanModel = cleanModel["models/".Length..];

        var method = stream ? "streamGenerateContent" : "generateContent";
        var alt = stream ? "&alt=sse" : "";
        return $"models/{Uri.EscapeDataString(cleanModel)}:{method}?key={Uri.EscapeDataString(apiKey.Trim())}{alt}";
    }

    private static GeminiGenerateContentRequest BuildRequest(
        IReadOnlyList<(string Role, string Content)> messages,
        GeminiOptions opts)
    {
        var systemParts = new List<GeminiPart>();
        var contents = new List<GeminiContent>();

        var configuredSystem = opts.SystemPrompt?.Trim();
        if (!string.IsNullOrEmpty(configuredSystem))
            systemParts.Add(new GeminiPart(configuredSystem));

        foreach (var (role, content) in messages)
        {
            var text = content?.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            if (string.Equals(role, "system", StringComparison.OrdinalIgnoreCase))
            {
                systemParts.Add(new GeminiPart(text));
                continue;
            }

            contents.Add(new GeminiContent(
                string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                [new GeminiPart(text)]));
        }

        if (contents.Count == 0)
            contents.Add(new GeminiContent("user", [new GeminiPart("Hello")]));

        return new GeminiGenerateContentRequest
        {
            SystemInstruction = systemParts.Count == 0 ? null : new GeminiSystemInstruction(systemParts),
            Contents = contents,
            GenerationConfig = BuildGenerationConfig(opts)
        };
    }

    private static GeminiGenerationConfig? BuildGenerationConfig(GeminiOptions opts)
    {
        var config = new GeminiGenerationConfig
        {
            MaxOutputTokens = opts.MaxOutputTokens is > 0 ? opts.MaxOutputTokens : null,
            Temperature = opts.Temperature,
            TopP = opts.TopP,
            TopK = opts.TopK is > 0 ? opts.TopK : null
        };

        return config.MaxOutputTokens is null &&
               config.Temperature is null &&
               config.TopP is null &&
               config.TopK is null
            ? null
            : config;
    }

    private static string? ExtractSseJson(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith(':'))
            return null;

        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["data:".Length..].Trim();

        if (trimmed.Length == 0 || trimmed == "[DONE]")
            return null;

        return trimmed.StartsWith('{') ? trimmed : null;
    }

    private static IEnumerable<string> ExtractTextPieces(string json)
    {
        var text = ExtractText(json);
        if (!string.IsNullOrEmpty(text))
            yield return text;
    }

    private static string ExtractText(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(json, JsonOptions);
            var parts = parsed?.Candidates?
                .SelectMany(c => c.Content?.Parts ?? [])
                .Select(p => p.Text)
                .Where(t => !string.IsNullOrEmpty(t));
            return string.Concat(parts ?? []);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string FormatGeminiHttpError(HttpStatusCode statusCode, string body)
    {
        var code = (int)statusCode;
        var detail = TryExtractGeminiError(body) ?? TruncateForDisplay(body, 700);
        if (string.IsNullOrWhiteSpace(detail))
            detail = "(empty response body)";

        return
            $"Gemini returned HTTP {code}.\r\n\r\n{detail}\r\n\r\n" +
            "Check Gemini__ApiKey, Gemini__DefaultModel, and that the Gemini API is enabled for your key.";
    }

    private static string? TryExtractGeminiError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();
            }
        }
        catch (JsonException)
        {
            // ignore
        }

        return null;
    }

    private static string TruncateForDisplay(string s, int maxLen)
    {
        var t = s.Trim();
        if (t.Length <= maxLen)
            return t;
        return t[..maxLen] + "...";
    }

    private sealed class GeminiGenerateContentRequest
    {
        public GeminiSystemInstruction? SystemInstruction { get; init; }
        public required List<GeminiContent> Contents { get; init; }
        public GeminiGenerationConfig? GenerationConfig { get; init; }
    }

    private sealed record GeminiSystemInstruction(List<GeminiPart> Parts);

    private sealed record GeminiContent(string Role, List<GeminiPart> Parts);

    private sealed record GeminiPart(string Text);

    private sealed class GeminiGenerationConfig
    {
        public int? MaxOutputTokens { get; init; }
        public double? Temperature { get; init; }
        public double? TopP { get; init; }
        public int? TopK { get; init; }
    }

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiResponseContent? Content { get; set; }
    }

    private sealed class GeminiResponseContent
    {
        public List<GeminiResponsePart>? Parts { get; set; }
    }

    private sealed class GeminiResponsePart
    {
        public string? Text { get; set; }
    }
}
