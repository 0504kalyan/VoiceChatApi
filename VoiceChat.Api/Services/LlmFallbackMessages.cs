namespace VoiceChat.Api.Services;

public static class LlmFallbackMessages
{
    /// <summary>Unexpected failure while streaming after a successful connection (rare).</summary>
    public static string Unavailable() =>
        "I couldn't get a response from Gemini.\r\n\r\n" +
        "Check:\r\n" +
        "• Gemini__ApiKey is configured on the API server\r\n" +
        "• Gemini__DefaultModel is a valid Gemini model, for example " + LlmRuntime.ProductDefaultChatModel + "\r\n" +
        "• The API server can reach https://generativelanguage.googleapis.com";

    public static string GeminiApiKeyMissing() =>
        "Gemini API key is missing.\r\n\r\n" +
        "Create an API key in Google AI Studio, then set Gemini__ApiKey on the API project/environment. " +
        "Do not put this key in Angular or any browser-side file.";

    public static string GeminiUnavailable(string? reason = null)
    {
        var extra = string.IsNullOrWhiteSpace(reason) ? "" : $"\r\nDetail: {reason.Trim()}";
        return
            $"Cannot reach Gemini.{extra}\r\n\r\n" +
            "• Check internet access from the API server\r\n" +
            "• Check Gemini__ApiKey is valid\r\n" +
            "• Check Gemini__DefaultModel is available for your key";
    }
}
