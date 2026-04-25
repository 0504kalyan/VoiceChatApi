namespace VoiceChat.Api.Options;

/// <summary>
/// Supabase hosts PostgreSQL. Set <c>SupabaseCredentials__ConnectionString</c> as an environment variable or in <c>.env</c>.
/// Process env <c>SupabaseCredentials__ConnectionString</c> maps to <c>SupabaseCredentials:ConnectionString</c>.
/// </summary>
public class SupabaseCredentialsOptions
{
    public const string SectionName = "SupabaseCredentials";

    public string ConnectionString { get; set; } = string.Empty;
}
