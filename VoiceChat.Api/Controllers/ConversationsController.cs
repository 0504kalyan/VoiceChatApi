using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VoiceChat.Api.Data;
using VoiceChat.Api.Models.Dtos;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Options;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ConversationsController(
    AppDbContext db,
    IOptions<OllamaOptions> ollamaOptions,
    ChatGenerationCancellationRegistry generationCancellation) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationListItemDto>>> List(CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var list = await db.Conversations
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new ConversationListItemDto(c.Id, c.Title, c.Model, c.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Ok(list);
    }

    [HttpPost]
    public async Task<ActionResult<ConversationListItemDto>> Create(
        [FromBody] CreateConversationRequest? body,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var now = DateTimeOffset.UtcNow;
        var conv = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = body?.Title,
            Model = string.IsNullOrWhiteSpace(body?.Model)
                ? LlmRuntime.DefaultChatModel(ollamaOptions.Value)
                : body.Model.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ConversationListItemDto(conv.Id, conv.Title, conv.Model, conv.UpdatedAt));
    }

    /// <summary>Update conversation fields (e.g. <see cref="Conversation.Model"/> for the next assistant reply).</summary>
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ConversationListItemDto>> Update(
        Guid id,
        [FromBody] UpdateConversationRequest? body,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var conv = await db.Conversations.FirstOrDefaultAsync(
            c => c.Id == id && c.UserId == userId,
            cancellationToken);
        if (conv is null)
            return NotFound();

        if (body is not null && !string.IsNullOrWhiteSpace(body.Model))
        {
            conv.Model = body.Model.Trim();
            if (conv.Model.Length > 100)
                return BadRequest(new { message = "Model name is too long." });
        }

        conv.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ConversationListItemDto(conv.Id, conv.Title, conv.Model, conv.UpdatedAt));
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> Messages(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var exists = await db.Conversations.AnyAsync(c => c.Id == id && c.UserId == userId,
            cancellationToken);
        if (!exists)
            return NotFound();

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new MessageDto(m.Id, m.Role, m.Content, m.InputMode, m.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(messages);
    }

    /// <summary>Request + assistant reply rows (text + JSON columns) for this conversation.</summary>
    [HttpGet("{id:guid}/response-archives")]
    public async Task<ActionResult<IReadOnlyList<ResponseArchiveDto>>> ResponseArchives(Guid id,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var exists = await db.Conversations.AnyAsync(c => c.Id == id && c.UserId == userId,
            cancellationToken);
        if (!exists)
            return NotFound();

        var rows = await db.RequestResponseArchives
            .AsNoTracking()
            .Where(a => a.ConversationId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new ResponseArchiveDto(a.Id, a.UserRequest, a.ResponseText, a.ResponseJson, a.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    /// <summary>Soft-delete: conversation hidden from lists; data retained.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId,
            cancellationToken);
        if (conv is null)
            return NotFound();
        conv.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Cancels in-flight assistant generation (same registry as SignalR <c>cancelGeneration</c>).
    /// Use this from the client so Stop works even when the hub pipeline is busy.
    /// </summary>
    [HttpPost("{id:guid}/cancel-generation")]
    public async Task<IActionResult> CancelGeneration(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var exists = await db.Conversations.AnyAsync(
            c => c.Id == id && c.UserId == userId,
            cancellationToken);
        if (!exists)
            return NotFound();
        generationCancellation.Cancel(id);
        return NoContent();
    }

    /// <summary>Remove this message and all later messages in the thread (for edit-resend flow).</summary>
    [HttpDelete("{conversationId:guid}/messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessagesFrom(Guid conversationId, Guid messageId,
        CancellationToken cancellationToken)
    {
        var userId = User.RequireUserId();
        var conv = await db.Conversations.FirstOrDefaultAsync(
            c => c.Id == conversationId && c.UserId == userId,
            cancellationToken);
        if (conv is null)
            return NotFound();

        var anchor = await db.Messages.FirstOrDefaultAsync(
            m => m.Id == messageId && m.ConversationId == conversationId,
            cancellationToken);
        if (anchor is null)
            return NotFound();

        var toRemove = await db.Messages
            .Where(m => m.ConversationId == conversationId && m.CreatedAt >= anchor.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var m in toRemove)
            m.IsActive = false;
        conv.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
