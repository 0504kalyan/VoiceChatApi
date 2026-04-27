namespace VoiceChat.Api.Models.Entities;

public class IdeWorkspace
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = "My Workspace";
    public string NormalizedName { get; set; } = "my workspace";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<IdeFile> Files { get; set; } = new List<IdeFile>();
}
