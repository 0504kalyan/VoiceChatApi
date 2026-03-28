namespace VoiceChat.Api.Interfaces;

public interface IChatOrchestrator
{
    IAsyncEnumerable<string> StreamAssistantReplyAsync(
        Guid conversationId,
        string userContent,
        string inputMode,
        CancellationToken cancellationToken = default);
}
