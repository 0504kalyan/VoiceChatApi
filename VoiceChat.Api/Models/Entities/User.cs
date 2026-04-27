namespace VoiceChat.Api.Models.Entities;

public class User
{
    public Guid Id { get; set; }

    /// <summary>Legacy/demo identifier; unique. New accounts use email: or google: prefix.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Google &quot;sub&quot; claim when the user signed in with Google (optional).</summary>
    public string? GoogleSub { get; set; }

    /// <summary>Gmail address (must be gmail.com / googlemail.com for OTP flows).</summary>
    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    /// <summary>True after email is verified (registration OTP or Google). Required for password login.</summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>ASP.NET Identity-compatible password hash (null for Google-only accounts until they set a password).</summary>
    public string? PasswordHash { get; set; }

    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When false, the account is soft-deleted and excluded from normal queries.</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<OtpVerification> OtpVerifications { get; set; } = new List<OtpVerification>();

    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();

    public ICollection<IdeWorkspace> IdeWorkspaces { get; set; } = new List<IdeWorkspace>();

    public ICollection<IdeFile> IdeFiles { get; set; } = new List<IdeFile>();
}

