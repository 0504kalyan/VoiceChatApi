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
    IOptions<GeminiOptions> geminiOptions,
    ILogger<ChatOrchestrator> logger) : IChatOrchestrator
{
    public const string ContinuationPrompt =
        "Continue from exactly where you stopped in the previous assistant response. Do not repeat content already written. " +
        "If you finish a code block, add the plain-text bullet-point explanation for that block after the fence.";

    private const string AutoContinuationPrompt =
        "Continue from exactly where you stopped. Do not repeat previous content. Complete the remaining task.";

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
        IReadOnlyList<LlmAttachment>? attachments = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var conversation = await db.Conversations.FirstOrDefaultAsync(
            c => c.Id == conversationId && c.UserId == userId,
            cancellationToken);
        if (conversation is null)
            throw new InvalidOperationException("Conversation not found.");

        var activeModel = LlmRuntime.NormalizeChatModel(conversation.Model, geminiOptions.Value)
            ?? LlmRuntime.DefaultChatModel(geminiOptions.Value);
        if (!string.Equals(conversation.Model, activeModel, StringComparison.Ordinal))
            conversation.Model = activeModel;

        var isContinuation = IsContinuationRequest(userContent) ||
                             string.Equals(inputMode, "continue", StringComparison.OrdinalIgnoreCase);
        Message? userMessage = null;
        Message? assistantToAppend = null;
        if (isContinuation)
        {
            assistantToAppend = await db.Messages
                .Where(m => m.ConversationId == conversationId && m.Role == "assistant" && !m.IsGenerationComplete)
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (assistantToAppend is null)
                throw new InvalidOperationException("No incomplete assistant message found to continue.");
        }
        else
        {
            var userMessageContent = BuildStoredUserMessage(userContent, attachments);
            userMessage = new Message
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                Role = "user",
                Content = userMessageContent,
                InputMode = inputMode is "voice" ? "voice" : "text",
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Messages.Add(userMessage);
            await db.SaveChangesAsync(cancellationToken);
        }

        var maxHistory = Math.Max(4, geminiOptions.Value.MaxHistoryMessages);
        var includeIncompleteAssistantContext = isContinuation;
        // Newest N messages only — avoids loading entire threads before the first model token.
        // Omit incomplete assistant messages except for explicit continuation requests.
        var rows = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .Where(m => m.Role != "assistant" || m.IsGenerationComplete || includeIncompleteAssistantContext)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxHistory)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(cancellationToken);
        var history = rows.Select(r => (r.Role, r.Content)).ToList();

        var messagesForLlm = new List<(string Role, string Content)>();
        var system = geminiOptions.Value.SystemPrompt?.Trim();
        if (!string.IsNullOrEmpty(system))
            messagesForLlm.Add(("system", system));
        messagesForLlm.AddRange(history);
        if (includeIncompleteAssistantContext)
            messagesForLlm.Add(("user", ContinuationPrompt));
        var baseMessagesForLlm = messagesForLlm.ToList();

        var buffer = new System.Text.StringBuilder();
        string? recoveryFallback = null;
        var stoppedEarly = false;
        var autoContinuationCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var requestMessages = BuildMessagesForCurrentGeneration(baseMessagesForLlm, buffer, autoContinuationCount);
            var outputLimitReached = false;

            await using var enumerator = llm.StreamChatAsync(
                    activeModel,
                    requestMessages,
                    autoContinuationCount == 0 ? attachments : null,
                    cancellationToken)
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
                        assistantToAppend,
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
                if (token == LlmStreamMarkers.OutputLimitReached)
                {
                    outputLimitReached = true;
                    continue;
                }
                buffer.Append(token);
                yield return token;
            }

            if (recoveryFallback is not null || !outputLimitReached)
                break;

            autoContinuationCount++;
            logger.LogInformation(
                "Gemini hit output limit for conversation {ConversationId}; automatically continuing response (part {Part}).",
                conversationId,
                autoContinuationCount + 1);
        }

        if (recoveryFallback is not null)
            yield return recoveryFallback;

        await PersistAssistantReplyAsync(
            conversationId,
            conversation,
            userMessage,
            assistantToAppend,
            buffer,
            stoppedEarly,
            cancellationToken);
    }

    private static IReadOnlyList<(string Role, string Content)> BuildMessagesForCurrentGeneration(
        IReadOnlyList<(string Role, string Content)> baseMessages,
        System.Text.StringBuilder generatedSoFar,
        int autoContinuationCount)
    {
        if (autoContinuationCount == 0 || generatedSoFar.Length == 0)
            return baseMessages;

        var messages = baseMessages.ToList();
        messages.Add(("assistant", generatedSoFar.ToString()));
        messages.Add(("user", AutoContinuationPrompt));
        return messages;
    }

    private async Task PersistAssistantReplyAsync(
        Guid conversationId,
        Conversation conversation,
        Message? userMessage,
        Message? assistantToAppend,
        System.Text.StringBuilder buffer,
        bool stoppedEarly,
        CancellationToken cancellationToken)
    {
        var replyText = buffer.ToString();
        if (string.IsNullOrWhiteSpace(replyText))
            return;

        var assistant = assistantToAppend ?? new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            InputMode = "text",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (assistantToAppend is null)
        {
            assistant.Content = replyText;
            db.Messages.Add(assistant);
        }
        else
        {
            assistant.Content += replyText;
        }
        assistant.IsGenerationComplete = !stoppedEarly;

        var archiveJson = JsonSerializer.Serialize(
            new
            {
                conversationId,
                model = conversation.Model,
                userRequest = userMessage?.Content ?? "Continue",
                assistantContent = replyText,
                userMessageId = userMessage?.Id,
                assistantMessageId = assistant.Id,
                userMessageCreatedAt = userMessage?.CreatedAt,
                assistantMessageCreatedAt = assistant.CreatedAt,
                stoppedEarly
            },
            JsonArchiveOptions);

        db.RequestResponseArchives.Add(new RequestResponseArchive
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            UserRequest = userMessage?.Content ?? "Continue",
            ResponseText = replyText,
            ResponseJson = archiveJson,
            CreatedAt = DateTimeOffset.UtcNow
        });

        conversation.UpdatedAt = DateTimeOffset.UtcNow;

        // Stopped early: use a quick title only and skip the extra LLM title call.
        if (userMessage is not null && string.IsNullOrWhiteSpace(conversation.Title))
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

            var model = LlmRuntime.NormalizeChatModel(conversation.Model, geminiOptions.Value)
                ?? LlmRuntime.DefaultChatModel(geminiOptions.Value);
            var generated = await llm.CompleteChatNonStreamingAsync(model, titlePrompt, cancellationToken);
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

    private static string BuildStoredUserMessage(string userContent, IReadOnlyList<LlmAttachment>? attachments)
    {
        if (IsContinuationRequest(userContent))
            return "Continue";

        var text = userContent.Trim();
        if (attachments is null || attachments.Count == 0)
            return text;

        var countText = attachments.Count == 1 ? "1 file attached" : $"{attachments.Count} files attached";
        if (string.IsNullOrWhiteSpace(text))
            return countText;

        return $"{text}\n\n{countText}";
    }

    private static bool IsContinuationRequest(string userContent) =>
        string.Equals(userContent.Trim(), ContinuationPrompt, StringComparison.Ordinal);

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
