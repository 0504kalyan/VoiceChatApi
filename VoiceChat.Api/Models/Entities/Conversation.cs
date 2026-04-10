namespace VoiceChat.Api.Models.Entities;

public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string? Title { get; set; }
    public string Model { get; set; } = "llama3.2";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When false, the conversation is soft-deleted (hidden from the user).</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<RequestResponseArchive> ResponseArchives { get; set; } = new List<RequestResponseArchive>();
}
