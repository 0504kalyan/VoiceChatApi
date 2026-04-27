using Microsoft.Extensions.Configuration;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Resolves the PostgreSQL/Npgsql connection string used by both runtime startup and EF CLI design-time commands.
/// </summary>
public static class DatabaseConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var conn = T(configuration["SupabaseCredentials:ConnectionString"])
            ?? T(configuration.GetConnectionString("DefaultConnection"))
            ?? T(Environment.GetEnvironmentVariable("SupabaseCredentials__ConnectionString"))
            ?? T(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"));

        if (string.IsNullOrWhiteSpace(conn))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string missing. Set environment variable SupabaseCredentials__ConnectionString or ConnectionStrings__DefaultConnection, " +
                "or add Api/VoiceChat.Api/.env with SupabaseCredentials__ConnectionString=..., or run: dotnet user-secrets set \"SupabaseCredentials:ConnectionString\" \"...\" --project VoiceChat.Api.csproj");
        }

        if (conn.StartsWith("{{", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SupabaseCredentials:ConnectionString is still a literal {{...}} placeholder. Use an empty value in appsettings and set SupabaseCredentials__ConnectionString to your Npgsql string (Host=...;Username=...;Password=...;Database=...;SSL Mode=Require;Trust Server Certificate=true).");
        }

        PostgresConnectionStringLogging.ThrowIfNotNpgsqlConnectionString(conn);
        NpgsqlSupabaseConnection.ThrowIfPoolerNeedsTenantUsername(conn);
        return NpgsqlSupabaseConnection.PrepareConnectionString(conn);
    }

    private static string? T(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
