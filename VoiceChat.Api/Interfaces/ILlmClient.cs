namespace VoiceChat.Api.Interfaces;

public interface ILlmClient
{
    IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default);

    /// <summary>Single non-streaming completion (e.g. short title). Returns null on failure.</summary>
    Task<string?> CompleteChatNonStreamingAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default);
}
