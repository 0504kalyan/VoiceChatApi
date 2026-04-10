using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260409120000_PasswordHashOtpUserRequired")]
public partial class PasswordHashOtpUserRequired : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            table: "Users",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.Sql("""
            DELETE FROM [dbo].[OtpVerifications] WHERE [UserId] IS NULL;
            """);

        // Index must be dropped before ALTER COLUMN; FK must be dropped before index if SQL Server requires it (FK dropped first is typical).
        migrationBuilder.DropForeignKey(
            name: "FK_OtpVerifications_Users_UserId",
            table: "OtpVerifications");

        migrationBuilder.DropIndex(
            name: "IX_OtpVerifications_UserId",
            table: "OtpVerifications");

        migrationBuilder.AlterColumn<Guid>(
            name: "UserId",
            table: "OtpVerifications",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OtpVerifications_UserId",
            table: "OtpVerifications",
            column: "UserId");

        migrationBuilder.AddForeignKey(
            name: "FK_OtpVerifications_Users_UserId",
            table: "OtpVerifications",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_OtpVerifications_Users_UserId",
            table: "OtpVerifications");

        migrationBuilder.DropIndex(
            name: "IX_OtpVerifications_UserId",
            table: "OtpVerifications");

        migrationBuilder.AlterColumn<Guid>(
            name: "UserId",
            table: "OtpVerifications",
            type: "uniqueidentifier",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier");

        migrationBuilder.CreateIndex(
            name: "IX_OtpVerifications_UserId",
            table: "OtpVerifications",
            column: "UserId");

        migrationBuilder.AddForeignKey(
            name: "FK_OtpVerifications_Users_UserId",
            table: "OtpVerifications",
            column: "UserId",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);

        migrationBuilder.DropColumn(
            name: "PasswordHash",
            table: "Users");
    }
}
