using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public static class LlmRuntime
{
    /// <summary>Default model tag when none is stored on the conversation.</summary>
    public static string DefaultChatModel(GeminiOptions options)
    {
        var m = options.DefaultModel?.Trim();
        return string.IsNullOrEmpty(m) ? ProductDefaultChatModel : m;
    }

    public static string? NormalizeChatModel(string? requested, GeminiOptions options)
    {
        var fallback = DefaultChatModel(options);
        var model = requested?.Trim();
        if (string.IsNullOrEmpty(model))
            return fallback;

        if (model.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            model = model["models/".Length..];

        return model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ? model : fallback;
    }

    /// <summary>Used when <see cref="GeminiOptions.DefaultModel"/> is not set in configuration.</summary>
    public const string ProductDefaultChatModel = "gemini-2.5-flash";
}
