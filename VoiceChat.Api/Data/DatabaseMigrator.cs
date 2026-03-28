using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VoiceChat.Api.Data;

/// <summary>
/// Applies pending EF Core migrations on startup so the database schema matches your model.
/// <para>
/// After you change entities, add a migration once: <c>dotnet ef migrations add Name --output-dir Data/Migrations</c>.
/// Startup calls <see cref="ApplyPendingMigrationsAsync"/>, which applies any pending migration files.
/// </para>
/// <para>
/// Also runs an idempotent SQL repair for the soft-delete / archive schema so existing databases stay consistent
/// if migration history was out of sync.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    public static async Task ApplyPendingMigrationsAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date (no pending EF Core migrations).");
        }
        else
        {
            logger.LogInformation(
                "Applying {Count} pending migration(s) to the database: {Migrations}",
                pending.Count,
                string.Join(", ", pending));

            await db.Database.MigrateAsync(cancellationToken);

            logger.LogInformation("EF Core migrations applied successfully.");
        }

        await EnsureSqlServerSoftDeleteAndArchiveAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Idempotent: ensures <c>Conversations</c> soft-delete columns, <c>RequestResponseArchives</c>, indexes, and migration history row exist.
    /// </summary>
    private static async Task EnsureSqlServerSoftDeleteAndArchiveAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        const string sql = """
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'Conversations' AND c.name = N'IsDeleted')
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [IsDeleted] bit NOT NULL DEFAULT 0;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'Conversations' AND c.name = N'DeletedAt')
BEGIN
    ALTER TABLE [dbo].[Conversations] ADD [DeletedAt] datetimeoffset NULL;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i
    INNER JOIN sys.tables t ON i.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'Conversations' AND i.name = N'IX_Conversations_IsDeleted')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Conversations_IsDeleted] ON [dbo].[Conversations] ([IsDeleted]);
END

IF NOT EXISTS (
    SELECT 1 FROM sys.tables t
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'RequestResponseArchives')
BEGIN
    CREATE TABLE [dbo].[RequestResponseArchives] (
        [Id] uniqueidentifier NOT NULL,
        [ConversationId] uniqueidentifier NOT NULL,
        [UserRequest] nvarchar(max) NOT NULL,
        [ResponseText] nvarchar(max) NOT NULL,
        [ResponseJson] nvarchar(max) NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL,
        CONSTRAINT [PK_RequestResponseArchives] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RequestResponseArchives_Conversations_ConversationId] FOREIGN KEY ([ConversationId])
            REFERENCES [dbo].[Conversations] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_RequestResponseArchives_ConversationId_CreatedAt]
        ON [dbo].[RequestResponseArchives] ([ConversationId], [CreatedAt]);
END

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260328061000_SoftDeleteAndResponseArchive')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260328061000_SoftDeleteAndResponseArchive', N'10.0.5');
END
""";

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.LogDebug("SQL Server schema compatibility script executed.");
        }
        catch (SqlException ex)
        {
            logger.LogError(ex,
                "Schema compatibility script failed. Check SQL Server permissions and that the Conversations table exists.");
            throw;
        }
    }
}
