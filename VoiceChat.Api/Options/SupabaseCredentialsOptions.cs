namespace VoiceChat.Api.Options;

/// <summary>
/// Supabase hosts PostgreSQL. Set <c>SupabaseCredentials__ConnectionString</c> (Render) or in <c>.env</c> locally.
/// Maps to <see cref="SupabaseCredentials"/>; <c>ConnectionStrings:DefaultConnection</c> uses <c>{{SupabaseCredentials:ConnectionString}}</c>.
/// </summary>
public class SupabaseCredentialsOptions
{
    public const string SectionName = "SupabaseCredentials";

    public string ConnectionString { get; set; } = string.Empty;
}
