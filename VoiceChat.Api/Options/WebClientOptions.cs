namespace VoiceChat.Api.Options;

/// <summary>Browser SPA origin used after Google OAuth (OTP verification page).</summary>
public class WebClientOptions
{
    public const string SectionName = "WebClient";

    public string PublicOrigin { get; set; } = "http://localhost:4200";
}
