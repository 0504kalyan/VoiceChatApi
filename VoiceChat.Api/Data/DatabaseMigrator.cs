using System.Net.Sockets;
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
        try
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
        catch (Exception ex) when (IsLikelySupabaseIpv4OnlyHostIssue(ex))
        {
            throw new InvalidOperationException(
                "Database connection failed: network unreachable to Supabase (often IPv6). " +
                "Many cloud hosts use IPv4 only; the direct host db.<project>.supabase.co may resolve only to IPv6. " +
                "Permanent fix: In Supabase → Connect, copy the Session pooler or Transaction pooler string " +
                "(host like aws-0-REGION.pooler.supabase.com; use Username postgres.<project-ref> when shown; port 5432 or 6543 per mode). " +
                "Or purchase Supabase IPv4 add-on for direct db host. See Api/SUPABASE.md (IPv6 section).",
                ex);
        }
    }

    /// <summary>
    /// Detects ENETUNREACH when connecting to IPv6-only endpoints from IPv4-only clouds.
    /// </summary>
    private static bool IsLikelySupabaseIpv4OnlyHostIssue(Exception ex)
    {
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is SocketException se && se.SocketErrorCode == SocketError.NetworkUnreachable)
                return true;
        }

        return false;
    }
}
