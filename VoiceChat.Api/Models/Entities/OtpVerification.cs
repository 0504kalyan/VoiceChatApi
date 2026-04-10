namespace VoiceChat.Api.Models.Entities;

public enum OtpPurpose : byte
{
    /// <summary>Email verification during registration (OTP row always has <see cref="OtpVerification.UserId"/> set).</summary>
    Register = 1
}

/// <summary>
/// One-time password challenge sent to a Gmail address (hashed at rest).
/// </summary>
public class OtpVerification
{
    public Guid Id { get; set; }

    /// <summary>Pending registration user (created before OTP is sent).</summary>
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the OTP code (hex).</summary>
    public string CodeHash { get; set; } = string.Empty;

    public OtpPurpose Purpose { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public int FailedAttemptCount { get; set; }

    /// <summary>When false, the OTP row is inactive (consumed, invalidated, or revoked).</summary>
    public bool IsActive { get; set; } = true;
}
