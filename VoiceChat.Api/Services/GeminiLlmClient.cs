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
        IReadOnlyList<LlmAttachment>? attachments = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            yield return LlmFallbackMessages.GeminiApiKeyMissing();
            yield break;
        }

        var resolved = ResolveModel(model);
        if (ShouldUseImageGeneration(messages, attachments, opts))
        {
            var imageResponse = await GenerateImageAsync(messages, attachments, opts, cancellationToken);
            yield return string.IsNullOrWhiteSpace(imageResponse)
                ? "I could not generate or enhance the image. Try a shorter prompt or a smaller image."
                : imageResponse;
            yield break;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildModelPath(resolved, stream: true, opts.ApiKey))
        {
            Content = JsonContent.Create(BuildRequest(messages, opts, attachments, includeGoogleSearchGrounding: true), options: JsonOptions)
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

                if (HasOutputLimitFinishReason(json))
                    yield return LlmStreamMarkers.OutputLimitReached;
            }
        }
    }

    private async Task<string?> GenerateImageAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<LlmAttachment>? attachments,
        GeminiOptions opts,
        CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(opts.ImageGenerationModel)
            ? "gemini-3.1-flash-image-preview"
            : opts.ImageGenerationModel.Trim();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            BuildModelPath(model, stream: false, opts.ApiKey))
        {
            Content = JsonContent.Create(
                BuildRequest(
                    messages,
                    opts,
                    attachments,
                    includeGoogleSearchGrounding: false,
                    includeImageResponse: true),
                options: JsonOptions)
        };

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or SocketException)
        {
            return LlmFallbackMessages.GeminiUnavailable(ex.Message);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return FormatGeminiHttpError(response.StatusCode, json);

            return ExtractTextAndImages(json);
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
            Content = JsonContent.Create(BuildRequest(messages, opts, attachments: null, includeGoogleSearchGrounding: false), options: JsonOptions)
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
        GeminiOptions opts,
        IReadOnlyList<LlmAttachment>? attachments,
        bool includeGoogleSearchGrounding,
        bool includeImageResponse = false)
    {
        var systemParts = new List<GeminiPart>();
        var contents = new List<GeminiContent>();

        var configuredSystem = opts.SystemPrompt?.Trim();
        if (!string.IsNullOrEmpty(configuredSystem))
            systemParts.Add(new GeminiPart(configuredSystem));
        systemParts.Add(new GeminiPart(BuildCurrentDateInstruction()));

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

        AddAttachmentsToLastUserMessage(contents, attachments);

        if (contents.Count == 0)
            contents.Add(new GeminiContent("user", [new GeminiPart("Hello")]));

        return new GeminiGenerateContentRequest
        {
            SystemInstruction = systemParts.Count == 0 ? null : new GeminiSystemInstruction(systemParts),
            Contents = contents,
            GenerationConfig = BuildGenerationConfig(opts, includeImageResponse),
            Tools = includeGoogleSearchGrounding && opts.EnableGoogleSearchGrounding
                ? [new GeminiTool { GoogleSearch = new GeminiGoogleSearch() }]
                : null
        };
    }

    private static string BuildCurrentDateInstruction()
    {
        var now = DateTimeOffset.UtcNow;
        return
            $"Current date/time (UTC): {now:dddd, MMMM d, yyyy HH:mm:ss} UTC.\n" +
            "If the user asks for the current date/time, answer from this value. " +
            "For latest news, current events, current prices, recent releases, or other time-sensitive facts, use Google Search grounding when available and mention when information may change.";
    }

    private static GeminiGenerationConfig? BuildGenerationConfig(GeminiOptions opts, bool includeImageResponse)
    {
        var config = new GeminiGenerationConfig
        {
            MaxOutputTokens = opts.MaxOutputTokens is > 0 ? opts.MaxOutputTokens : null,
            Temperature = opts.Temperature,
            TopP = opts.TopP,
            TopK = opts.TopK is > 0 ? opts.TopK : null,
            ResponseModalities = includeImageResponse ? ["TEXT", "IMAGE"] : null,
            ImageConfig = includeImageResponse
                ? new GeminiImageConfig(
                    string.IsNullOrWhiteSpace(opts.ImageAspectRatio) ? "1:1" : opts.ImageAspectRatio.Trim(),
                    string.IsNullOrWhiteSpace(opts.ImageSize) ? "1K" : opts.ImageSize.Trim())
                : null
        };

        return config.MaxOutputTokens is null &&
               config.Temperature is null &&
               config.TopP is null &&
               config.TopK is null &&
               config.ResponseModalities is null &&
               config.ImageConfig is null
            ? null
            : config;
    }

    private static bool ShouldUseImageGeneration(
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<LlmAttachment>? attachments,
        GeminiOptions opts)
    {
        if (!opts.EnableImageGeneration)
            return false;

        var prompt = messages.LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase)).Content;
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var text = prompt.ToLowerInvariant();
        var wantsImageOutput =
            text.Contains("create image") ||
            text.Contains("create an image") ||
            text.Contains("generate image") ||
            text.Contains("generate an image") ||
            text.Contains("draw ") ||
            text.Contains("make an image") ||
            text.Contains("design an image") ||
            text.Contains("return the same image") ||
            text.Contains("enhance") ||
            text.Contains("upscale") ||
            text.Contains("restore image") ||
            text.Contains("improve this image");

        if (!wantsImageOutput)
            return false;

        var hasImageInput = attachments?.Any(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) == true;
        return hasImageInput || text.Contains("image") || text.Contains("picture") || text.Contains("photo");
    }

    private static void AddAttachmentsToLastUserMessage(
        List<GeminiContent> contents,
        IReadOnlyList<LlmAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return;

        var userContent = contents.LastOrDefault(c => string.Equals(c.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (userContent is null)
        {
            userContent = new GeminiContent("user", [new GeminiPart("Please review the attached files.")]);
            contents.Add(userContent);
        }

        foreach (var attachment in attachments.Take(6))
        {
            if (string.IsNullOrWhiteSpace(attachment.Base64Data))
                continue;

            userContent.Parts.Add(new GeminiPart
            {
                InlineData = new GeminiInlineData(
                    string.IsNullOrWhiteSpace(attachment.ContentType)
                        ? "application/octet-stream"
                        : attachment.ContentType,
                    attachment.Base64Data)
            });
        }
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

    private static bool HasOutputLimitFinishReason(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(json, JsonOptions);
            return parsed?.Candidates?.Any(c =>
                string.Equals(c.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string ExtractTextAndImages(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(json, JsonOptions);
            var output = new List<string>();
            foreach (var part in parsed?.Candidates?.SelectMany(c => c.Content?.Parts ?? []) ?? [])
            {
                if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    output.Add(part.Text.Trim());
                    continue;
                }

                if (part.InlineData is { Data.Length: > 0 } image)
                {
                    var mime = string.IsNullOrWhiteSpace(image.MimeType) ? "image/png" : image.MimeType;
                    output.Add($"![Generated image](data:{mime};base64,{image.Data})");
                }
            }

            return string.Join("\n\n", output.Where(x => !string.IsNullOrWhiteSpace(x)));
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

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            var retryText = ExtractRetryText(detail);
            return
                "Gemini image/text generation quota is currently exhausted.\r\n\r\n" +
                "This happens when the Google AI Studio/Gemini API key has reached its free-tier or project rate limit. " +
                "Enable billing, wait for quota to reset, or use another Gemini API key/project with available image-generation quota." +
                (retryText is null ? string.Empty : $"\r\n\r\n{retryText}");
        }

        return
            $"Gemini returned HTTP {code}.\r\n\r\n{detail}\r\n\r\n" +
            "Check Gemini__ApiKey, Gemini__DefaultModel, and that the Gemini API is enabled for your key.";
    }

    private static string? ExtractRetryText(string detail)
    {
        const string marker = "Please retry in ";
        var start = detail.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        var end = detail.IndexOf('.', start + marker.Length);
        if (end < 0)
            return null;

        var retry = detail[start..(end + 1)].Trim();
        return retry.Length == 0 ? null : retry;
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
        public List<GeminiTool>? Tools { get; init; }
    }

    private sealed record GeminiSystemInstruction(List<GeminiPart> Parts);

    private sealed record GeminiContent(string Role, List<GeminiPart> Parts);

    private sealed class GeminiPart
    {
        public GeminiPart()
        {
        }

        public GeminiPart(string text)
        {
            Text = text;
        }

        public string? Text { get; init; }
        public GeminiInlineData? InlineData { get; init; }
    }

    private sealed record GeminiInlineData(string MimeType, string Data);

    private sealed class GeminiGenerationConfig
    {
        public int? MaxOutputTokens { get; init; }
        public double? Temperature { get; init; }
        public double? TopP { get; init; }
        public int? TopK { get; init; }
        public string[]? ResponseModalities { get; init; }
        public GeminiImageConfig? ImageConfig { get; init; }
    }

    private sealed record GeminiImageConfig(string AspectRatio, string ImageSize);

    private sealed class GeminiTool
    {
        [JsonPropertyName("google_search")]
        public GeminiGoogleSearch? GoogleSearch { get; init; }
    }

    private sealed class GeminiGoogleSearch;

    private sealed class GeminiGenerateContentResponse
    {
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        public GeminiResponseContent? Content { get; set; }
        public string? FinishReason { get; set; }
    }

    private sealed class GeminiResponseContent
    {
        public List<GeminiResponsePart>? Parts { get; set; }
    }

    private sealed class GeminiResponsePart
    {
        public string? Text { get; set; }
        public GeminiInlineData? InlineData { get; set; }
    }
}
