using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;

namespace VoiceChat.Api.Infrastructure;

/// <summary>
/// Logs a single startup line for SQL Server connectivity checks without exposing secrets.
/// </summary>
public static class SqlServerConnectionStringLogging
{
    /// <summary>
    /// Render and most cloud hosts run Linux. Local dev strings (Server=.; Trusted_Connection) cannot work there.
    /// </summary>
    public static void ThrowIfProductionUsesIncompatibleSql(IHostEnvironment env, string connectionString)
    {
        if (!env.IsProduction())
            return;

        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (b.IntegratedSecurity)
            {
                throw new InvalidOperationException(
                    "Production SQL: Trusted_Connection / Integrated Security is Windows-only and does not work on Render (Linux). " +
                    "Use SQL authentication: User Id=...; Password=...; Encrypt=True (Azure SQL / RDS). " +
                    "Set ConnectionStrings__DefaultConnection in the Render dashboard.");
            }

            var ds = (b.DataSource ?? string.Empty).Trim();
            if (ds is "." or "" ||
                ds.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
                ds.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                ds.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production SQL: Server is set to a machine-local host (., localhost, localdb). " +
                    "Point ConnectionStrings__DefaultConnection at your cloud SQL Server (e.g. YOURSERVER.database.windows.net).");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // If the string cannot be parsed, let the driver fail later with a clearer message.
        }
    }

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
