namespace VoiceChat.Api.Options;

/// <summary>
/// Local inference via <a href="https://ollama.com">Ollama</a> — no cloud API keys or usage billing.
/// Bind from section <see cref="SectionName"/> or env vars <c>Ollama__BaseUrl</c>, <c>Ollama__DefaultModel</c>, etc.
/// </summary>
/// <remarks>
/// <para><b>Model picks</b> (run <c>ollama pull &lt;name&gt;</c> first):</para>
/// <list type="bullet">
/// <item><description><c>llama3.2</c> — default library tag (good balance); matches <see cref="LlmRuntime.ProductDefaultChatModel"/>.</description></item>
/// <item><description><c>llama3.2:1b</c> or <c>gemma2:2b</c> — smaller / faster.</description></item>
/// <item><description><c>qwen2.5:3b</c> — strong for code.</description></item>
/// </list>
/// </remarks>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL of the Ollama HTTP API (default local install).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Default model name for new chats (must exist locally — <c>ollama pull &lt;name&gt;</c>).
    /// </summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>Max messages to send to Ollama (newest). Lower = smaller prompts and faster prefill.</summary>
    public int MaxHistoryMessages { get; set; } = 20;

    /// <summary>Passed to Ollama as <c>num_ctx</c>. Lower = faster time-to-first-token and less RAM (try 2048–4096).</summary>
    public int NumCtx { get; set; } = 2048;

    /// <summary>Optional <c>num_predict</c> cap — limits max output tokens (shorter = faster end-to-end).</summary>
    public int? NumPredict { get; set; }

    /// <summary>
    /// How long Ollama keeps the model loaded between requests (e.g. <c>30m</c>, <c>1h</c>). Reduces cold-load latency in production.
    /// </summary>
    public string KeepAlive { get; set; } = "30m";

    /// <summary>Optional <c>temperature</c> (omit from request when null).</summary>
    public double? Temperature { get; set; }

    /// <summary>Optional <c>top_p</c> nucleus sampling.</summary>
    public double? TopP { get; set; }

    /// <summary>Optional <c>top_k</c> sampling.</summary>
    public int? TopK { get; set; }

    /// <summary>Optional <c>num_batch</c> — can improve throughput on GPU (model-dependent).</summary>
    public int? NumBatch { get; set; }

    /// <summary>
    /// Sent as the first <c>system</c> message to Ollama only (not persisted in message history).
    /// Set to empty to disable. Override in configuration for product tone.
    /// </summary>
    public string SystemPrompt { get; set; } = DefaultAssistantSystemPrompt;

    /// <summary>Built-in default when <see cref="SystemPrompt"/> is not set in configuration.</summary>
    public const string DefaultAssistantSystemPrompt =
        "You are VoiceChat's AI assistant. Be clear, accurate, and concise unless the user asks for depth.\n" +
        "Do not fixate on a single interpretation: when a problem could be solved in several ways, briefly note alternatives " +
        "or trade-offs where useful. For bugs and errors, consider multiple plausible causes before recommending a fix.\n" +
        "When the user changes topic or asks a new question, answer that directly without over-fitting to earlier turns.\n" +
        "For technical fixes: prefer safe, verifiable steps and code.";

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
