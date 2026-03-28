namespace VoiceChat.Api.Interfaces;

public interface ILlmClient
{
    IAsyncEnumerable<string> StreamChatAsync(
        string model,
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken cancellationToken = default);
}
