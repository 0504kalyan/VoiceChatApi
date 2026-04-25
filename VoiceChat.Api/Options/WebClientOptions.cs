namespace VoiceChat.Api.Options;

/// <summary>
/// Optional browser SPA origin fallback used for redirects when the current request does not provide Origin/Referer.
/// </summary>
public class WebClientOptions
{
    public const string SectionName = "WebClient";

    public string PublicOrigin { get; set; } = string.Empty;
}
