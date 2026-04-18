using Microsoft.Extensions.Hosting;
using Npgsql;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Startup line for PostgreSQL (Supabase) connectivity — no passwords logged.
/// </summary>
public static class PostgresConnectionStringLogging
{
    public static string FormatForConsole(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "PostgreSQL: (empty — set SupabaseCredentials__ConnectionString or ConnectionStrings__DefaultConnection)";
        }

        try
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString);
            return $"PostgreSQL: Host={b.Host}; Port={b.Port}; Database={b.Database}; SSL={b.SslMode}";
        }
        catch
        {
            return "PostgreSQL: (connection string could not be parsed)";
        }
    }

    /// <summary>
    /// Render/Linux production should not use a localhost-only DB string by mistake.
    /// </summary>
    public static void ThrowIfProductionUsesLocalOnlyHost(IHostEnvironment env, string connectionString)
    {
        if (!env.IsProduction())
            return;

        try
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString);
            var host = (b.Host ?? string.Empty).Trim();
            if (host is "" or "127.0.0.1" or "::1" ||
                host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production PostgreSQL Host points to localhost. Set SupabaseCredentials__ConnectionString to your Supabase pooler or direct host (e.g. aws-0-....pooler.supabase.com).");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // Let Npgsql fail later if the string is invalid.
        }
    }
}
