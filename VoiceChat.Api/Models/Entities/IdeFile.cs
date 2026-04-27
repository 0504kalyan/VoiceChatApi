namespace VoiceChat.Api.Models.Entities;

public class IdeFile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid WorkspaceId { get; set; }
    public IdeWorkspace Workspace { get; set; } = null!;
    public string Path { get; set; } = string.Empty;
    public string NormalizedPath { get; set; } = string.Empty;
    public string Language { get; set; } = "plaintext";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}
