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

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<RequestResponseArchive> ResponseArchives { get; set; } = new List<RequestResponseArchive>();
}
