using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VoiceChat.Api.Data;

/// <summary>
/// Applies pending EF Core migrations on startup (PostgreSQL / Supabase).
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
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s) to the database: {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("EF Core migrations applied successfully.");
    }
}
