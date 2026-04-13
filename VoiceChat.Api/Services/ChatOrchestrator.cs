using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
        Guid userId,
        string userContent,
        string inputMode,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            c => c.Id == conversationId && c.UserId == userId,
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
        // Newest N messages only — avoids loading entire threads before the first model token.
        // Omit assistant messages that were cut off by "Stop" — they confuse the model and add tokens for the next turn.
        var rows = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Where(m => m.Role != "assistant" || m.IsGenerationComplete)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxHistory)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);
        var history = rows.Select(r => (r.Role, r.Content)).ToList();

        var messagesForLlm = new List<(string Role, string Content)>();
        var system = ollamaOptions.Value.SystemPrompt?.Trim();
        if (!string.IsNullOrEmpty(system))
            messagesForLlm.Add(("system", system));
        messagesForLlm.AddRange(history);

        var buffer = new System.Text.StringBuilder();
        string? recoveryFallback = null;

        await using var enumerator = llm.StreamChatAsync(conversation.Model, messagesForLlm, cancellationToken)
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
            CreatedAt = DateTimeOffset.UtcNow,
            IsGenerationComplete = !stoppedEarly
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

        // Stopped early: use a quick title only — skip the extra LLM title call so Ollama is free for the user's next prompt.
        if (string.IsNullOrWhiteSpace(conversation.Title))
        {
            if (stoppedEarly)
                conversation.Title = BuildFallbackTitleFromUserMessage(userMessage.Content);
            else
                await TrySetSummarizedTitleAsync(conversation, userMessage.Content, replyText, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>ChatGPT-style short title from first exchange; falls back to truncated user text if LLM fails.</summary>
    private async Task TrySetSummarizedTitleAsync(
        Conversation conversation,
        string userContent,
        string assistantContent,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackTitleFromUserMessage(userContent);
        try
        {
            var titlePrompt = new List<(string Role, string Content)>
            {
                (
                    "system",
                    "You write very short chat titles only. Reply with a title of at most 8 words. " +
                    "No quotation marks around the title. No preamble or explanation."
                ),
                (
                    "user",
                    "Propose one concise title for this conversation.\n\nUser message:\n" +
                    TruncateForTitle(userContent, 700) +
                    "\n\nAssistant reply (may be long):\n" +
                    TruncateForTitle(assistantContent, 700)
                )
            };

            var generated = await llm.CompleteChatNonStreamingAsync(conversation.Model, titlePrompt, cancellationToken);
            var cleaned = SanitizeGeneratedTitle(generated);
            conversation.Title = !string.IsNullOrWhiteSpace(cleaned) ? cleaned : fallback;
        }
        catch (OperationCanceledException)
        {
            conversation.Title = fallback;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Summarized title failed for conversation {ConversationId}", conversation.Id);
            conversation.Title = fallback;
        }

        if (conversation.Title != null && conversation.Title.Length > 500)
            conversation.Title = conversation.Title[..500].Trim();
    }

    private static string BuildFallbackTitleFromUserMessage(string userContent)
    {
        var t = userContent.ReplaceLineEndings(" ").Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length <= 80)
            return t;
        return t[..77].TrimEnd() + "…";
    }

    private static string TruncateForTitle(string s, int max)
    {
        var t = s.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private static string? SanitizeGeneratedTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim().Trim('"', '\'', '`');
        t = t.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var parts = t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        t = parts.Length > 0 ? parts[0] : t.Trim();
        if (t.StartsWith("Title:", StringComparison.OrdinalIgnoreCase))
            t = t[6..].Trim();
        return t.Length > 120 ? t[..120].Trim() : t;
    }
}
