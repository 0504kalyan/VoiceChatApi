using Microsoft.EntityFrameworkCore;
using VoiceChat.Api.Models.Entities;

namespace VoiceChat.Api.Data;

/// <summary>
/// Maps to SQL Server tables: Users, Conversations, Messages (see Data/Migrations).
/// Startup runs <see cref="SqlServerDatabaseBootstrap.EnsureDatabaseExistsAsync"/> then applies migrations to <c>ConnectionStrings:DefaultConnection</c>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<RequestResponseArchive> RequestResponseArchives => Set<RequestResponseArchive>();
    public DbSet<OtpVerification> OtpVerifications => Set<OtpVerification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExternalId).IsUnique().HasFilter("[IsActive] = 1");
            e.Property(x => x.ExternalId).HasMaxLength(255);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.GoogleSub).HasMaxLength(128);
            e.HasIndex(x => x.GoogleSub).IsUnique()
                .HasFilter("[GoogleSub] IS NOT NULL AND [IsActive] = 1");
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.NormalizedEmail).HasMaxLength(320);
            e.HasIndex(x => x.NormalizedEmail).IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL AND [IsActive] = 1");
            e.Property(x => x.PasswordHash).HasColumnType("nvarchar(max)");
            e.HasQueryFilter(u => u.IsActive);
        });

        modelBuilder.Entity<OtpVerification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.NormalizedEmail).HasMaxLength(320);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.Property(x => x.Purpose).HasConversion<byte>();
            e.HasIndex(x => new { x.NormalizedEmail, x.Purpose, x.ExpiresAt });
            e.HasOne(x => x.User)
                .WithMany(u => u.OtpVerifications)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(o => o.IsActive);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash);
            e.HasIndex(x => new { x.UserId, x.ExpiresAt });
            e.HasOne(x => x.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(t => t.IsActive);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasIndex(x => x.IsActive);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(c => c.IsActive);
        });

        modelBuilder.Entity<RequestResponseArchive>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserRequest).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResponseText).HasColumnType("nvarchar(max)");
            e.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation).WithMany(x => x.ResponseArchives).HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(a => a.IsActive);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.InputMode).HasMaxLength(20);
            e.Property(x => x.IsGenerationComplete).HasDefaultValue(true);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(m => m.IsActive);
        });
    }
}
