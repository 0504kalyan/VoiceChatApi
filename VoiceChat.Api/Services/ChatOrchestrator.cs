using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;

namespace VoiceChat.Api.Services;

public class ChatOrchestrator(
    AppDbContext db,
    ILlmClient llm,
    IOptions<OllamaOptions> ollamaOptions,
    ILogger<ChatOrchestrator> logger) : IChatOrchestrator
{
    private static readonly JsonSerializerOptions JsonArchiveOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async IAsyncEnumerable<string> StreamAssistantReplyAsync(
        Guid conversationId,
        string userContent,
        string inputMode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            c => c.Id == conversationId && !c.IsDeleted,
            cancellationToken);
        if (conversation is null)
            throw new InvalidOperationException("Conversation not found.");

        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userContent,
            InputMode = inputMode is "voice" ? "voice" : "text",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Messages.Add(userMessage);
        await db.SaveChangesAsync(cancellationToken);

        var maxHistory = Math.Max(4, ollamaOptions.Value.MaxHistoryMessages);
        var rows = await db.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);
        if (rows.Count > maxHistory)
            rows = rows.Skip(rows.Count - maxHistory).ToList();
        var history = rows.Select(r => (r.Role, r.Content)).ToList();

        var buffer = new System.Text.StringBuilder();
        string? recoveryFallback = null;

        await using var enumerator = llm.StreamChatAsync(conversation.Model, history, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException)
            {
                // Commit partial text without the request token — it is already canceled and would skip SaveChanges.
                await PersistAssistantReplyAsync(
                    conversationId,
                    conversation,
                    userMessage,
                    buffer,
                    stoppedEarly: true,
                    CancellationToken.None);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LLM stream failed for conversation {ConversationId}", conversationId);
                buffer.Clear();
                recoveryFallback = LlmFallbackMessages.Unavailable();
                buffer.Append(recoveryFallback);
                break;
            }

            if (!hasNext)
                break;

            var token = enumerator.Current;
            buffer.Append(token);
            yield return token;
        }

        if (recoveryFallback is not null)
            yield return recoveryFallback;

        await PersistAssistantReplyAsync(
            conversationId,
            conversation,
            userMessage,
            buffer,
            stoppedEarly: false,
            cancellationToken);
    }

    private async Task PersistAssistantReplyAsync(
        Guid conversationId,
        Conversation conversation,
        Message userMessage,
        System.Text.StringBuilder buffer,
        bool stoppedEarly,
        CancellationToken cancellationToken)
    {
        var replyText = buffer.ToString();
        if (string.IsNullOrWhiteSpace(replyText))
            return;

        var assistant = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = replyText,
            InputMode = "text",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Messages.Add(assistant);

        var archiveJson = JsonSerializer.Serialize(
            new
            {
                conversationId,
                model = conversation.Model,
                userRequest = userMessage.Content,
                assistantContent = replyText,
                userMessageId = userMessage.Id,
                assistantMessageId = assistant.Id,
                userMessageCreatedAt = userMessage.CreatedAt,
                assistantMessageCreatedAt = assistant.CreatedAt,
                stoppedEarly
            },
            JsonArchiveOptions);

        db.RequestResponseArchives.Add(new RequestResponseArchive
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            UserRequest = userMessage.Content,
            ResponseText = replyText,
            ResponseJson = archiveJson,
            CreatedAt = DateTimeOffset.UtcNow
        });

        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(conversation.Title) && userMessage.Content.Length > 0)
            conversation.Title = userMessage.Content.Length <= 80 ? userMessage.Content : userMessage.Content[..80] + "…";

        await db.SaveChangesAsync(cancellationToken);
    }
}
