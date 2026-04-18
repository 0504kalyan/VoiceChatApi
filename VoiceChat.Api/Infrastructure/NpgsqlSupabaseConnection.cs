using Npgsql;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Supabase pooler (Supavisor) and Docker-friendly Npgsql defaults.
/// </summary>
public static class NpgsqlSupabaseConnection
{
    /// <summary>
    /// Disables GSS/Kerberos so slim Linux images (e.g. Render) do not require libgssapi_krb5. Supabase uses SCRAM password auth.
    /// </summary>
    public static string PrepareConnectionString(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable
        };
        return b.ConnectionString;
    }

    /// <summary>
    /// Pooler hosts require <c>Username=postgres.&lt;project_ref&gt;</c> so the tenant is identified (ENOIDENTIFIER otherwise).
    /// </summary>
    public static void ThrowIfPoolerNeedsTenantUsername(string connectionString)
    {
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        var host = b.Host ?? string.Empty;
        if (!host.Contains("pooler.supabase.com", StringComparison.OrdinalIgnoreCase))
            return;

        var user = (b.Username ?? string.Empty).Trim();
        if (user.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            !user.StartsWith("postgres.", StringComparison.OrdinalIgnoreCase) ||
            user.Length <= "postgres.".Length)
        {
            throw new InvalidOperationException(
                "Supabase pooler requires Username=postgres.YOUR_PROJECT_REF (from Dashboard → Connect → Session pooler). " +
                "Plain \"postgres\" is rejected with (ENOIDENTIFIER) no tenant identifier. Copy the full username from Supabase.");
        }
    }
}
