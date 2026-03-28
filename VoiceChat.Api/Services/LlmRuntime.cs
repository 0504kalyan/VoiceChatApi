using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public static class LlmRuntime
{
    /// <summary>Default model tag when none is stored on the conversation.</summary>
    public static string DefaultChatModel(OllamaOptions options)
    {
        var m = options.DefaultModel?.Trim();
        return string.IsNullOrEmpty(m) ? ProductDefaultChatModel : m;
    }

    /// <summary>Used when <see cref="OllamaOptions.DefaultModel"/> is not set in configuration.</summary>
    public const string ProductDefaultChatModel = "llama3.2";
}
