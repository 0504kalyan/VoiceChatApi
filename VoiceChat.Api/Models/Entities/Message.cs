namespace VoiceChat.Api.Models.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string InputMode { get; set; } = "text";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When false, the message is soft-deleted (e.g. trimmed from edit-resend flow).</summary>
    public bool IsActive { get; set; } = true;
}
