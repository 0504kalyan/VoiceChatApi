namespace VoiceChat.Api.Models.Entities;

/// <summary>
/// One-time link token for password reset (raw token is emailed; only hash is stored).
/// </summary>
public class PasswordResetToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 hex of the raw token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>When false, the token row is inactive (used or superseded).</summary>
    public bool IsActive { get; set; } = true;
}
