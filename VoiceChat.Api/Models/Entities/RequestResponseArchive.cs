namespace VoiceChat.Api.Models.Entities;

/// <summary>
/// Stores each assistant reply tied to the user request: plain text plus a JSON snapshot for tooling/export.
/// </summary>
public class RequestResponseArchive
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;

    /// <summary>User prompt / request text.</summary>
    public string UserRequest { get; set; } = string.Empty;

    /// <summary>Assistant reply as plain text (same as chat display).</summary>
    public string ResponseText { get; set; } = string.Empty;

    /// <summary>Structured JSON snapshot (ids, model, timestamps, content).</summary>
    public string ResponseJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When false, the archive row is soft-deleted.</summary>
    public bool IsActive { get; set; } = true;
}
