using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public static class LlmFallbackMessages
{
    /// <summary>Unexpected failure while streaming after a successful connection (rare).</summary>
    public static string Unavailable() =>
        "I couldn't get a response from the local AI (Ollama).\r\n\r\n" +
        "Check:\r\n" +
        "• Ollama is installed and running — https://ollama.com\r\n" +
        "• Ollama:BaseUrl in appsettings matches your server (default http://localhost:11434)\r\n" +
        "• You pulled a model: ollama pull " + LlmRuntime.ProductDefaultChatModel + "\r\n\r\n" +
        "Browse models: https://ollama.com/library";

    public static string OllamaUnreachable(OllamaOptions options, string? reason = null)
    {
        var url = options.ResolveBaseUri().ToString().TrimEnd('/');
        var extra = string.IsNullOrWhiteSpace(reason) ? "" : $"\r\nDetail: {reason.Trim()}";
        return
            $"Cannot reach Ollama at {url}.{extra}\r\n\r\n" +
            "• Install Ollama from https://ollama.com and start the app\r\n" +
            "• Run: ollama pull " + LlmRuntime.ProductDefaultChatModel + "\r\n" +
            "• If Ollama runs elsewhere, set Ollama:BaseUrl (or Ollama__BaseUrl)";
    }
}
