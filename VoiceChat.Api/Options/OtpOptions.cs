namespace VoiceChat.Api.Options;

public class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>HMAC-style secret for OTP hashing. If empty, Jwt:SigningKey is used at runtime.</summary>
    public string Pepper { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 10;
}
