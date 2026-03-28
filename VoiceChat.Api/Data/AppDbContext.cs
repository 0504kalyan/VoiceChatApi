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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.ExternalId).IsUnique();
            e.Property(x => x.ExternalId).HasMaxLength(255);
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
            e.HasIndex(x => x.IsDeleted);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
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
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(20);
            e.Property(x => x.InputMode).HasMaxLength(20);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
