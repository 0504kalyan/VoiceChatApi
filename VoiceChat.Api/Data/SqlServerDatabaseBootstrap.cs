using Microsoft.Data.SqlClient;

namespace VoiceChat.Api.Data;

/// <summary>
/// Ensures the database catalog from the connection string exists on SQL Server before EF migrations run.
/// </summary>
public static class SqlServerDatabaseBootstrap
{
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
