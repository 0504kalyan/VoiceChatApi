using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20260409051000_AuthOtpAndGoogle")]
public partial class AuthOtpAndGoogle : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "EmailConfirmed",
            table: "Users",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "Email",
            table: "Users",
            type: "nvarchar(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "GoogleSub",
            table: "Users",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedEmail",
            table: "Users",
            type: "nvarchar(320)",
            maxLength: 320,
            nullable: true);

        migrationBuilder.Sql("UPDATE [dbo].[Users] SET [EmailConfirmed] = 1");

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

        migrationBuilder.CreateTable(
            name: "OtpVerifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                NormalizedEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Purpose = table.Column<byte>(type: "tinyint", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                FailedAttemptCount = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OtpVerifications", x => x.Id);
                table.ForeignKey(
                    name: "FK_OtpVerifications_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OtpVerifications_NormalizedEmail_Purpose_ExpiresAt",
            table: "OtpVerifications",
            columns: new[] { "NormalizedEmail", "Purpose", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_OtpVerifications_UserId",
            table: "OtpVerifications",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "OtpVerifications");

        migrationBuilder.DropIndex(
            name: "IX_Users_GoogleSub",
            table: "Users");

        migrationBuilder.DropIndex(
            name: "IX_Users_NormalizedEmail",
            table: "Users");

        migrationBuilder.DropColumn(name: "EmailConfirmed", table: "Users");
        migrationBuilder.DropColumn(name: "Email", table: "Users");
        migrationBuilder.DropColumn(name: "GoogleSub", table: "Users");
        migrationBuilder.DropColumn(name: "NormalizedEmail", table: "Users");
    }
}
