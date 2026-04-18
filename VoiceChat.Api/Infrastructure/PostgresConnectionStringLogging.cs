using Microsoft.Extensions.Hosting;
using Npgsql;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Startup line for PostgreSQL (Supabase) connectivity — no passwords logged.
/// </summary>
public static class PostgresConnectionStringLogging
{
    /// <summary>
    /// Fails fast if Render/env still has a SQL Server string or anything Npgsql cannot parse.
    /// </summary>
    public static void ThrowIfNotNpgsqlConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        if (LooksLikeSqlServerConnectionString(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string looks like SQL Server (e.g. Trusted_Connection or Initial Catalog). This API uses PostgreSQL/Npgsql only. " +
                "On Render, replace ConnectionStrings__DefaultConnection with your Supabase Postgres string, for example: " +
                "Host=db.xxxxx.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=YOUR_PASSWORD;SSL Mode=Require;Trust Server Certificate=true");
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Connection string is not valid for Npgsql/PostgreSQL. Use Host=, Port=, Database=, Username=, Password=, SSL Mode=. " +
                "Remove SQL Server keywords. Supabase: Dashboard → Connect or Database → Connection string.", ex);
        }

        static bool LooksLikeSqlServerConnectionString(string s) =>
            s.Contains("Trusted_Connection", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Integrated Security", StringComparison.OrdinalIgnoreCase)
            || s.Contains("MultipleActiveResultSets", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Initial Catalog", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatForConsole(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "PostgreSQL: (empty — set SupabaseCredentials__ConnectionString or ConnectionStrings__DefaultConnection)";
        }

        try
        {
            var b = new NpgsqlConnectionStringBuilder(connectionString);
            var user = string.IsNullOrEmpty(b.Username) ? "(none)" : b.Username;
            var pwdHint = DescribePasswordForLog(b.Password);
            return
                $"PostgreSQL: Host={b.Host}; Port={b.Port}; Database={b.Database}; Username={user}; Password={pwdHint}; SSL={b.SslMode}";
        }
        catch
        {
            return "PostgreSQL: (connection string could not be parsed — check for SQL Server keywords like Trusted_Connection)";
        }
    }

    /// <summary>
    /// Never logs the password. Use length-only hint so 28P01 can be debugged without exposing secrets.
    /// </summary>
    private static string DescribePasswordForLog(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return "EMPTY — set Password= in user secrets / .env / Render (error 28P01 = wrong or missing password)";
        return $"set, length {password.Length} (value never logged)";
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
