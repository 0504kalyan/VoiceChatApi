namespace VoiceChat.Api.Options;

/// <summary>
/// Google Gemini Developer API configuration.
/// Bind from section <see cref="SectionName"/> or env vars <c>Gemini__ApiKey</c>, <c>Gemini__DefaultModel</c>, etc.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Gemini Developer API key from Google AI Studio. Never put this in Angular/browser code.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Base URL of the Gemini REST API.</summary>
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>Default model name for new chats.</summary>
    public string DefaultModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Max messages to send to Gemini (newest). Lower = smaller prompts and lower token cost.</summary>
    public int MaxHistoryMessages { get; set; } = 20;

    /// <summary>Optional output-token cap sent as <c>maxOutputTokens</c>. Leave empty to let Gemini generate up to the model/provider limit.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Optional creativity value sent as <c>temperature</c>.</summary>
    public double? Temperature { get; set; } = 0.7;

    /// <summary>Optional nucleus sampling value sent as <c>topP</c>.</summary>
    public double? TopP { get; set; } = 0.9;

    /// <summary>Optional <c>topK</c> sampling.</summary>
    public int? TopK { get; set; }

    /// <summary>Enables image generation/editing when the user asks to create or enhance images.</summary>
    public bool EnableImageGeneration { get; set; } = true;

    /// <summary>Fast Gemini image model used for create/enhance image requests.</summary>
    public string ImageGenerationModel { get; set; } = "gemini-3.1-flash-image-preview";

    /// <summary>Image output aspect ratio for generated/enhanced images.</summary>
    public string ImageAspectRatio { get; set; } = "1:1";

    /// <summary>Image output size. Use 1K for faster responses; increase to 2K for quality.</summary>
    public string ImageSize { get; set; } = "1K";

    /// <summary>
    /// Enables Gemini Google Search grounding for current/latest/news style questions.
    /// Requires a model that supports the <c>google_search</c> tool.
    /// </summary>
    public bool EnableGoogleSearchGrounding { get; set; } = true;

    /// <summary>Models shown in the UI dropdown. Override with <c>Gemini__AvailableModels__0</c>, etc.</summary>
    public string[] AvailableModels { get; set; } =
    [
        "gemini-2.5-flash",
        "gemini-2.5-flash-lite",
        "gemini-2.5-pro",
        "gemini-2.0-flash"
    ];

    /// <summary>
    /// Sent as Gemini's system instruction only (not persisted in message history).
    /// Set to empty to disable. Override in configuration for product tone.
    /// </summary>
    public string SystemPrompt { get; set; } = DefaultAssistantSystemPrompt;

    /// <summary>Built-in default when <see cref="SystemPrompt"/> is not set in configuration.</summary>
    public const string DefaultAssistantSystemPrompt =
        "You are VoiceChat's AI assistant. Be clear, accurate, and concise unless the user asks for depth.\n" +
        "You can answer general questions, explain concepts, and generate code snippets when asked.\n" +
        "For technical fixes: prefer safe, verifiable steps and code. When a problem has multiple plausible causes, briefly note the main alternatives.\n" +
        "When you provide code, keep code inside fenced code blocks only. After each code block, add a short plain-text explanation as bullet points or numbered points, not a long paragraph. Cover what the block does, why key methods/classes/declarations are used, and what behavior or output to expect.";

    public Uri ResolveBaseUri()
    {
        var raw = BaseUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(raw))
            raw = DefaultBaseUrl;
        if (!raw.EndsWith('/'))
            raw += "/";
        return new Uri(raw);
    }

    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
}
