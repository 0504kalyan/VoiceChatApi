namespace VoiceChat.Api.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VoiceChat.Api";
    public string Audience { get; set; } = "VoiceChat.Web";

    /// <summary>Symmetric key (at least 32 characters for HS256).</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 10080; // 7 days
}
