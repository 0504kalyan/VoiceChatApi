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
        var ollamaRuntimeOptions = new Dictionary<string, int>();
        if (opts.NumCtx > 0)
            ollamaRuntimeOptions["num_ctx"] = opts.NumCtx;
        if (opts.NumPredict is { } np && np > 0)
            ollamaRuntimeOptions["num_predict"] = np;

        object payload = ollamaRuntimeOptions.Count > 0
            ? new
            {
                model = resolved,
                stream = true,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
                options = ollamaRuntimeOptions
            }
            : new
            {
                model = resolved,
                stream = true,
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList()
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = JsonContent.Create(payload, options: SerializeOptions)
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
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                yield return FormatOllamaHttpError(response.StatusCode, body);
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

    private sealed class OllamaChatStreamChunk
    {
        public OllamaDeltaMessage? Message { get; set; }
        public bool Done { get; set; }
    }

    private sealed class OllamaDeltaMessage
    {
        public string? Content { get; set; }
    }
}
