using Microsoft.Data.SqlClient;

namespace VoiceChat.Api.Data;

/// <summary>
/// Ensures the database catalog from the connection string exists on SQL Server before EF migrations run.
/// </summary>
public static class SqlServerDatabaseBootstrap
{
    /// <summary>
    /// Azure SQL Database does not allow <c>CREATE DATABASE</c> from user connections the way on-prem SQL Server does.
    /// Skip catalog creation and create the database in the Azure portal (or use an empty/elastic pool) instead.
    /// </summary>
    public static bool ShouldAttemptCreateCatalog(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var host = builder.DataSource ?? string.Empty;
            if (host.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase))
                return false;
            if (host.Contains("database.chinacloudapi.cn", StringComparison.OrdinalIgnoreCase))
                return false;
            if (host.Contains("database.usgovcloudapi.net", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            return;

        var dbNameForLiteral = databaseName.Replace("'", "''");
        var dbNameBracketed = "[" + databaseName.Replace("]", "]]") + "]";

        builder.InitialCatalog = "master";
        builder.Remove("AttachDbFilename");

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{dbNameForLiteral}')
                CREATE DATABASE {dbNameBracketed};
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
