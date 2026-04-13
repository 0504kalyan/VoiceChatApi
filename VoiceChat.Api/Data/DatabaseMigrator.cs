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
/// Also runs an idempotent SQL repair for legacy databases (e.g. <c>RequestResponseArchives</c>, <c>IsActive</c> columns).
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

        await EnsureSqlServerLegacySchemaAsync(db, logger, cancellationToken);
        await EnsureUsersPasswordHashColumnAsync(db, logger, cancellationToken);
    }

    /// <summary>
    /// Idempotent: ensures legacy <c>RequestResponseArchives</c> table / <c>IsActive</c> column where needed. Does not reference removed columns like <c>IsDeleted</c>.
    /// </summary>
    private static async Task EnsureSqlServerLegacySchemaAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        const string sql = """
-- Legacy: RequestResponseArchives table
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
        [IsActive] bit NOT NULL CONSTRAINT [DF_RequestResponseArchives_IsActive] DEFAULT 1,
        CONSTRAINT [PK_RequestResponseArchives] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RequestResponseArchives_Conversations_ConversationId] FOREIGN KEY ([ConversationId])
            REFERENCES [dbo].[Conversations] ([Id]) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX [IX_RequestResponseArchives_ConversationId_CreatedAt]
        ON [dbo].[RequestResponseArchives] ([ConversationId], [CreatedAt]);
END

-- Conversations use IsActive only; schema changes come from EF migration 20260409230000_IsActiveSoftDelete.
-- Do not reference IsDeleted here: SQL Server validates the whole batch, so any [IsDeleted] text fails after that column is dropped.

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'RequestResponseArchives' AND c.name = N'IsActive')
   AND EXISTS (
    SELECT 1 FROM sys.tables t2
    INNER JOIN sys.schemas s2 ON t2.schema_id = s2.schema_id
    WHERE s2.name = N'dbo' AND t2.name = N'RequestResponseArchives')
BEGIN
    ALTER TABLE [dbo].[RequestResponseArchives] ADD [IsActive] bit NOT NULL CONSTRAINT [DF_RequestResponseArchives_IsActive2] DEFAULT 1;
END

-- Messages.IsGenerationComplete (EF migration 20260410113000); idempotent for DBs missing the column
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'Messages' AND c.name = N'IsGenerationComplete')
   AND EXISTS (
    SELECT 1 FROM sys.tables t2
    INNER JOIN sys.schemas s2 ON t2.schema_id = s2.schema_id
    WHERE s2.name = N'dbo' AND t2.name = N'Messages')
BEGIN
    ALTER TABLE [dbo].[Messages] ADD [IsGenerationComplete] bit NOT NULL CONSTRAINT [DF_Messages_IsGenerationComplete] DEFAULT 1;
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

    /// <summary>
    /// Idempotent: adds <c>Users.PasswordHash</c> when missing (e.g. DB created before EF migration applied).
    /// </summary>
    private static async Task EnsureUsersPasswordHashColumnAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        const string sql = """
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name = N'Users' AND c.name = N'PasswordHash')
BEGIN
    ALTER TABLE [dbo].[Users] ADD [PasswordHash] nvarchar(max) NULL;
END
""";

        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            logger.LogDebug("Users.PasswordHash column compatibility check executed.");
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "Could not ensure Users.PasswordHash column exists.");
        }
    }
}
