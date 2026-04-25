namespace VoiceChat.Api.Interfaces;

public interface IChatOrchestrator
{
    IAsyncEnumerable<string> StreamAssistantReplyAsync(
        Guid conversationId,
        Guid userId,
        string userContent,
        string inputMode,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken cancellationToken = default);
}
