using Microsoft.EntityFrameworkCore;
using VoiceChat.Api.Models.Entities;
using VoiceChat.Api.Services;

namespace VoiceChat.Api.Data;

public static class DatabaseSeed
{
    public static async Task EnsureDemoUserAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(u => u.ExternalId == DemoUser.ExternalId, cancellationToken))
            return;

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            ExternalId = DemoUser.ExternalId,
            DisplayName = "Local Demo",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
