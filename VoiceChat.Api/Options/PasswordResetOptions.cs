namespace VoiceChat.Api.Options;

public class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    /// <summary>How long the reset link remains valid.</summary>
    public int ExpiryMinutes { get; set; } = 60;
}
