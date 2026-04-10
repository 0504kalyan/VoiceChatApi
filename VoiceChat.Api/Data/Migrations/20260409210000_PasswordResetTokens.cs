using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260409210000_PasswordResetTokens")]
public partial class PasswordResetTokens : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PasswordResetTokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                table.ForeignKey(
                    name: "FK_PasswordResetTokens_Users_UserId",
                    column: x => x.UserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTokens_TokenHash",
            table: "PasswordResetTokens",
            column: "TokenHash");

        migrationBuilder.CreateIndex(
            name: "IX_PasswordResetTokens_UserId_ExpiresAt",
            table: "PasswordResetTokens",
            columns: new[] { "UserId", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PasswordResetTokens");
    }
}
