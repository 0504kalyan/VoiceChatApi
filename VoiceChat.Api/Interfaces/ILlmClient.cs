namespace VoiceChat.Api.Interfaces;

public sealed record LlmAttachment(string FileName, string ContentType, string Base64Data);

public static class LlmStreamMarkers
{
    public const string OutputLimitReached = "\uE000VOICECHAT_OUTPUT_LIMIT_REACHED\uE000";
}

public interface ILlmClient
{
    IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken cancellationToken = default);

    /// <summary>Single non-streaming completion (e.g. short title). Returns null on failure.</summary>
    Task<string?> CompleteChatNonStreamingAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default);
}
