namespace VoiceChat.Api.Options;

/// <summary>
/// Local inference via <a href="https://ollama.com">Ollama</a> — no cloud API keys or usage billing.
/// Bind from section <see cref="SectionName"/> or env vars <c>Ollama__BaseUrl</c>, <c>Ollama__DefaultModel</c>.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL of the Ollama HTTP API (default local install).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Default model tag for new chats (e.g. <c>llama3.2</c>, <c>phi3</c>, <c>mistral</c>).
    /// Install with <c>ollama pull &lt;name&gt;</c>; list with <c>ollama list</c> or GET <c>/api/health/ollama/models</c>.
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>Max messages to send to Ollama (newest). Lower = faster prompts on long threads.</summary>
    public int MaxHistoryMessages { get; set; } = 32;

    /// <summary>Passed to Ollama as <c>num_ctx</c>. Smaller values often reduce time-to-first-token.</summary>
    public int NumCtx { get; set; } = 4096;

    /// <summary>Optional <c>num_predict</c> cap (null = model default).</summary>
    public int? NumPredict { get; set; }

    public Uri ResolveBaseUri()
    {
        var raw = BaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw))
            raw = DefaultLocalBaseUrl;
        if (!raw.EndsWith('/'))
            raw += "/";
        return new Uri(raw);
    }

    /// <summary>Typical URL when Ollama runs on the same machine.</summary>
    public const string DefaultLocalBaseUrl = "http://localhost:11434/";
}
