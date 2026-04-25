using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VoiceChat.Api.Interfaces;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Hubs;

/// <summary>
/// SendMessage returns immediately so <see cref="CancelGeneration"/> can run while the assistant streams.
/// Streaming uses <see cref="IHubContext{THub}"/> and a fresh DI scope so the hub instance is not used after SendMessage returns.
/// </summary>
[Authorize]
public class ChatHub(
    IServiceScopeFactory scopeFactory,
    ChatGenerationCancellationRegistry cancellationRegistry,
    IHubContext<ChatHub> hubContext,
    ILogger<ChatHub> log) : Hub
{
    public async Task JoinConversation(Guid conversationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, conversationId.ToString());
    }

    public async Task LeaveConversation(Guid conversationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId.ToString());
    }

    /// <summary>Stops the current assistant stream for this conversation (user clicked Stop).</summary>
    [HubMethodName("cancelGeneration")]
    public Task CancelGeneration(Guid conversationId)
    {
        cancellationRegistry.Cancel(conversationId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns immediately so the client can invoke <c>cancelGeneration</c> while Gemini is still streaming.
    /// (SignalR otherwise processes hub calls on the same connection one at a time.)
    /// </summary>
    public Task SendMessage(Guid conversationId, string content, string inputMode, IReadOnlyList<ChatUploadAttachment>? attachments = null)
    {
        var group = conversationId.ToString();
        var connectionAborted = Context.ConnectionAborted;
        var callerConnectionId = Context.ConnectionId;
        var cancelToken = cancellationRegistry.Register(conversationId);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(connectionAborted, cancelToken);
        var ct = linked.Token;

        _ = RunAssistantStreamAsync(
            group,
            conversationId,
            content,
            inputMode,
            NormalizeAttachments(attachments),
            linked,
            ct,
            connectionAborted,
            callerConnectionId);

        return Task.CompletedTask;
    }

    private async Task RunAssistantStreamAsync(
        string group,
        Guid conversationId,
        string content,
        string inputMode,
        IReadOnlyList<LlmAttachment> attachments,
        CancellationTokenSource linked,
        CancellationToken ct,
        CancellationToken connectionAborted,
        string callerConnectionId)
    {
        try
        {
            if (Context.User?.Identity?.IsAuthenticated != true)
                throw new HubException("Unauthorized.");
            var userId = Context.User.RequireUserId();
            await using var scope = scopeFactory.CreateAsyncScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IChatOrchestrator>();

            await foreach (var token in orchestrator.StreamAssistantReplyAsync(conversationId, userId, content, inputMode, attachments, ct))
            {
                await hubContext.Clients.Group(group).SendAsync("ReceiveToken", conversationId, token, cancellationToken: ct);
            }

            await hubContext.Clients.Group(group)
                .SendAsync("ReceiveComplete", conversationId, cancellationToken: connectionAborted);
        }
        catch (OperationCanceledException)
        {
            log.LogInformation("Assistant stream cancelled for conversation {ConversationId}", conversationId);
            await hubContext.Clients.Group(group)
                .SendAsync("ReceiveCancelled", conversationId, cancellationToken: connectionAborted);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            await hubContext.Clients.Client(callerConnectionId).SendAsync("ReceiveError", conversationId,
                "This conversation is no longer available.",
                connectionAborted);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Chat stream failed for {ConversationId}", conversationId);
            var msg =
                "Sorry, something went wrong while processing your message. Please try again.";
            await hubContext.Clients.Group(group).SendAsync("ReceiveToken", conversationId, msg,
                cancellationToken: connectionAborted);
            await hubContext.Clients.Group(group).SendAsync("ReceiveComplete", conversationId,
                cancellationToken: connectionAborted);
        }
        finally
        {
            linked.Dispose();
            cancellationRegistry.Complete(conversationId);
        }
    }

    private static IReadOnlyList<LlmAttachment> NormalizeAttachments(IReadOnlyList<ChatUploadAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return [];

        return attachments
            .Where(a => !string.IsNullOrWhiteSpace(a.Base64Data))
            .Take(6)
            .Select(a => new LlmAttachment(
                string.IsNullOrWhiteSpace(a.FileName) ? "attachment" : a.FileName.Trim(),
                string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType.Trim(),
                a.Base64Data.Trim()))
            .ToList();
    }
}

public sealed class ChatUploadAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Base64Data { get; set; } = string.Empty;
}
