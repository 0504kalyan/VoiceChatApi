using Microsoft.EntityFrameworkCore;
using VoiceChat.Api.Data;

namespace VoiceChat.Api.Services;

public static class DemoUser
{
    public const string ExternalId = "local-demo";

    public static async Task<Guid> GetIdAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.ExternalId == ExternalId, cancellationToken);
        return user.Id;
    }
}
