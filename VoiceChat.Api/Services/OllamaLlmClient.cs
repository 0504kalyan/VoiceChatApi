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
/// Streams chat from a local Ollama server (<c>POST /api/chat</c>). Uses models you have pulled locally — no paid APIs.
/// </summary>
public class OllamaLlmClient(HttpClient http, IOptions<OllamaOptions> options) : ILlmClient
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolved = ResolveModel(model);
        var opts = options.Value;
        var runtimeOptions = BuildRuntimeOptions(opts);
        var messagePayload = messages.Select(m => new OllamaApiMessage { Role = m.Role, Content = m.Content }).ToList();

        var body = new OllamaChatRequest
        {
            Model = resolved,
            Stream = true,
            Messages = messagePayload,
            Options = runtimeOptions.Count > 0 ? runtimeOptions : null,
            KeepAlive = string.IsNullOrWhiteSpace(opts.KeepAlive) ? null : opts.KeepAlive.Trim()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(body, options: SerializeOptions)
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
            connectionError = LlmFallbackMessages.OllamaUnreachable(options.Value, ex.Message);
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
                var bodyText = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return FormatOllamaHttpError(response.StatusCode, bodyText);
                yield break;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                OllamaChatStreamChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<OllamaChatStreamChunk>(line, DeserializeOptions);
                }
                catch
                {
                    continue;
                }

                if (chunk?.Message?.Content is { Length: > 0 } piece)
                    yield return piece;

                if (chunk?.Done == true)
                    yield break;
            }
        }
    }

    /// <inheritdoc />
    public async Task<string?> CompleteChatNonStreamingAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default)
    {
        var resolved = ResolveModel(model);
        var opts = options.Value;
        var messagePayload = messages.Select(m => new OllamaApiMessage { Role = m.Role, Content = m.Content }).ToList();

        var titleOptions = new Dictionary<string, object>
        {
            ["num_ctx"] = 1024,
            ["num_predict"] = 64,
            ["temperature"] = 0.35
        };

        var body = new OllamaChatRequest
        {
            Model = resolved,
            Stream = false,
            Messages = messagePayload,
            Options = titleOptions,
            KeepAlive = string.IsNullOrWhiteSpace(opts.KeepAlive) ? null : opts.KeepAlive.Trim()
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(body, options: SerializeOptions)
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
            try
            {
                var parsed = JsonSerializer.Deserialize<OllamaNonStreamChatResponse>(json, DeserializeOptions);
                var text = parsed?.Message?.Content?.Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    private static Dictionary<string, object> BuildRuntimeOptions(OllamaOptions opts)
    {
        var o = new Dictionary<string, object>();
        if (opts.NumCtx > 0)
            o["num_ctx"] = opts.NumCtx;
        if (opts.NumPredict is { } np && np > 0)
            o["num_predict"] = np;
        if (opts.Temperature is { } t)
            o["temperature"] = t;
        if (opts.TopP is { } tp)
            o["top_p"] = tp;
        if (opts.TopK is { } tk)
            o["top_k"] = tk;
        if (opts.NumBatch is { } nb && nb > 0)
            o["num_batch"] = nb;
        return o;
    }

    private string ResolveModel(string requested)
    {
        var fallback = LlmRuntime.DefaultChatModel(options.Value);
        if (string.IsNullOrWhiteSpace(requested))
            return fallback;
        return requested.Trim();
    }

    private static string FormatOllamaHttpError(HttpStatusCode statusCode, string body)
    {
        var code = (int)statusCode;
        var detail = TryExtractOllamaError(body) ?? TruncateForDisplay(body, 600);
        if (string.IsNullOrWhiteSpace(detail))
            detail = "(empty response body)";

        return
            $"Ollama returned HTTP {code}.\r\n\r\n{detail}\r\n\r\n" +
            "Pull the model if needed: ollama pull <model>\r\n" +
            "See https://ollama.com/library for available models.";
    }

    private static string? TryExtractOllamaError(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString();
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
        return t[..maxLen] + "…";
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("messages")]
        public required List<OllamaApiMessage> Messages { get; init; }

        [JsonPropertyName("options")]
        public Dictionary<string, object>? Options { get; init; }

        [JsonPropertyName("keep_alive")]
        public string? KeepAlive { get; init; }
    }

    private sealed class OllamaApiMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class OllamaChatStreamChunk
    {
        public OllamaDeltaMessage? Message { get; set; }
        public bool Done { get; set; }
    }

    private sealed class OllamaNonStreamChatResponse
    {
        public OllamaDeltaMessage? Message { get; set; }
    }

    private sealed class OllamaDeltaMessage
    {
        public string? Content { get; set; }
    }
}
