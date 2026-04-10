namespace VoiceChat.Api.Options;

/// <summary>
/// Google OAuth 2.0 <strong>Sign-in</strong> (console.cloud.google.com → APIs &amp; Services → Credentials → OAuth client).
/// This is unrelated to <see cref="EmailOptions"/> / SMTP: those settings only send email; they do not set OAuth redirect URIs.
/// <para>
/// The ASP.NET Core Google handler uses <c>CallbackPath = /signin-google</c> on the <strong>API</strong> origin (not the Angular port).
/// Authorized redirect URIs in Google Cloud must match exactly, including scheme and port, for example:
/// <c>http://localhost:5292/signin-google</c> and/or <c>https://localhost:7059/signin-google</c> (see launchSettings / how you open the API).
/// </para>
/// Prefer user secrets: <c>dotnet user-secrets set "Google:ClientId" "..."</c> and <c>Google:ClientSecret</c>.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "Google";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
