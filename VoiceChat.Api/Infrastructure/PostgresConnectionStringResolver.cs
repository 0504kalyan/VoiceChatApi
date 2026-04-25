using System.Text;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Picks the first valid Npgsql connection string from common configuration keys (Render, user secrets, .env).
/// Normalizes copy-paste issues (BOM, outer quotes, whitespace) and skips unresolved <c>{{...}}</c> placeholders.
/// </summary>
public static class PostgresConnectionStringResolver
{
    /// <summary>Trim, strip UTF-8 BOM / zero-width chars, remove one layer of matching outer quotes.</summary>
    public static string? Normalize(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var s = connectionString.Trim().TrimStart('\uFEFF', '\u200B').Trim();
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
        {
            s = s[1..^1].Trim().TrimStart('\uFEFF', '\u200B').Trim();
        }

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public static bool IsUnresolvedPlaceholder(string s) =>
        s.StartsWith("{{", StringComparison.Ordinal) && s.EndsWith("}}", StringComparison.Ordinal);

    /// <summary>
    /// Tries, in order: <c>ConnectionStrings:DefaultConnection</c>, <c>SupabaseCredentials:ConnectionString</c>,
    /// <c>SupabaseCredentials:DefaultConnection</c>. Returns the first value Npgsql can parse.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        Exception? firstNpgsqlError = null;
        foreach (var raw in EnumerateRawCandidates(configuration))
        {
            var n = Normalize(raw);
            if (string.IsNullOrWhiteSpace(n) || IsUnresolvedPlaceholder(n))
                continue;

            if (PostgresConnectionStringLogging.LooksLikeSqlServerConnectionString(n))
            {
                throw new InvalidOperationException(
                    "Connection string looks like SQL Server (e.g. Trusted_Connection or Initial Catalog). This API uses PostgreSQL/Npgsql only. " +
                    "Set SupabaseCredentials__ConnectionString (or ConnectionStrings__DefaultConnection) to a Supabase/Npgsql string.");
            }

            try
            {
                _ = new NpgsqlConnectionStringBuilder(n);
                return n;
            }
            catch (Exception ex)
            {
                firstNpgsqlError ??= ex;
            }
        }

        throw BuildResolveFailure(configuration, firstNpgsqlError);
    }

    private static IEnumerable<string?> EnumerateRawCandidates(IConfiguration configuration)
    {
        yield return configuration.GetConnectionString("DefaultConnection");
        yield return configuration["SupabaseCredentials:ConnectionString"];
        yield return configuration["SupabaseCredentials:DefaultConnection"];
    }

    private static Exception BuildResolveFailure(IConfiguration configuration, Exception? npgsqlInner)
    {
        var sb = new StringBuilder()
            .Append(
                "Could not obtain a valid PostgreSQL (Npgsql) connection string. Checked, in order: " +
                "ConnectionStrings__DefaultConnection, SupabaseCredentials__ConnectionString, SupabaseCredentials__DefaultConnection. ");

        var dc = Normalize(configuration.GetConnectionString("DefaultConnection"));
        if (dc is not null && IsUnresolvedPlaceholder(dc))
        {
            sb.Append(
                "ConnectionStrings:DefaultConnection is still the literal placeholder {{SupabaseCredentials:ConnectionString}} — " +
                "set SupabaseCredentials__ConnectionString (or clear a bad ConnectionStrings__DefaultConnection). ");
        }

        if (npgsqlInner is not null)
        {
            sb.Append("Npgsql parse error on the first non-empty candidate: ").Append(npgsqlInner.Message).Append(". ");
            sb.Append(
                "If it says \"starting at index 0\", remove a leading BOM or stray quote, and ensure the value starts with Host= or postgresql://.");
        }
        else
        {
            sb.Append("All candidates were empty, whitespace, or unresolved {{...}} placeholders.");
        }

        return new InvalidOperationException(sb.ToString().TrimEnd(), npgsqlInner);
    }
}
