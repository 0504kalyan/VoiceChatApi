using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260409230000_IsActiveSoftDelete")]
public partial class IsActiveSoftDelete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.DropIndex(
            name: "IX_Users_ExternalId",
            table: "Users");

        migrationBuilder.DropIndex(
            name: "IX_Users_GoogleSub",
            table: "Users");

        migrationBuilder.DropIndex(
            name: "IX_Users_NormalizedEmail",
            table: "Users");

        migrationBuilder.Sql("""
            CREATE UNIQUE NONCLUSTERED INDEX [IX_Users_ExternalId]
            ON [dbo].[Users]([ExternalId])
            WHERE [IsActive] = 1;

            CREATE UNIQUE NONCLUSTERED INDEX [IX_Users_GoogleSub]
            ON [dbo].[Users]([GoogleSub])
            WHERE [GoogleSub] IS NOT NULL AND [IsActive] = 1;

            CREATE UNIQUE NONCLUSTERED INDEX [IX_Users_NormalizedEmail]
            ON [dbo].[Users]([NormalizedEmail])
            WHERE [NormalizedEmail] IS NOT NULL AND [IsActive] = 1;
            """);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Messages",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "RequestResponseArchives",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "OtpVerifications",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "PasswordResetTokens",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.Sql("""
            UPDATE [dbo].[OtpVerifications] SET [IsActive] = CASE WHEN [ConsumedAt] IS NOT NULL THEN 0 ELSE 1 END;
            """);

        migrationBuilder.Sql("""
            UPDATE [dbo].[PasswordResetTokens] SET [IsActive] = CASE WHEN [UsedAt] IS NOT NULL THEN 0 ELSE 1 END;
            """);

        migrationBuilder.AddColumn<bool>(
            name: "IsActive",
            table: "Conversations",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.Sql("""
            UPDATE [dbo].[Conversations] SET [IsActive] = CASE WHEN [IsDeleted] = 1 THEN 0 ELSE 1 END;
            """);

        migrationBuilder.DropIndex(
            name: "IX_Conversations_IsDeleted",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Conversations");

        migrationBuilder.CreateIndex(
            name: "IX_Conversations_IsActive",
            table: "Conversations",
            column: "IsActive");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Conversations_IsActive",
            table: "Conversations");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeletedAt",
            table: "Conversations",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Conversations",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            UPDATE [dbo].[Conversations] SET [IsDeleted] = CASE WHEN [IsActive] = 0 THEN 1 ELSE 0 END;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Conversations_IsDeleted",
            table: "Conversations",
            column: "IsDeleted");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Conversations");

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS [IX_Users_ExternalId] ON [dbo].[Users];
            DROP INDEX IF EXISTS [IX_Users_GoogleSub] ON [dbo].[Users];
            DROP INDEX IF EXISTS [IX_Users_NormalizedEmail] ON [dbo].[Users];
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Users_ExternalId",
            table: "Users",
            column: "ExternalId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Users_GoogleSub",
            table: "Users",
            column: "GoogleSub",
            unique: true,
            filter: "[GoogleSub] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Users_NormalizedEmail",
            table: "Users",
            column: "NormalizedEmail",
            unique: true,
            filter: "[NormalizedEmail] IS NOT NULL");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Users");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "Messages");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "RequestResponseArchives");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "OtpVerifications");

        migrationBuilder.DropColumn(
            name: "IsActive",
            table: "PasswordResetTokens");
    }
}
