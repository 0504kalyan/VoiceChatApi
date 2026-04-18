using Microsoft.Data.SqlClient;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Logs a single startup line for SQL Server connectivity checks without exposing secrets.
/// </summary>
public static class SqlServerConnectionStringLogging
{
    public static string FormatForConsole(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "SQL Server: (empty — set ConnectionStrings__DefaultConnection in Render or .env for local dev)";
        }

        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            var auth = string.IsNullOrEmpty(b.UserID)
                ? (b.IntegratedSecurity ? "IntegratedSecurity" : "auth unset")
                : "SqlAuthentication";
            return $"SQL Server: Server={b.DataSource}; Database={b.InitialCatalog}; Encrypt={b.Encrypt}; Auth={auth}";
        }
        catch
        {
            return "SQL Server: (connection string could not be parsed)";
        }
    }
}
