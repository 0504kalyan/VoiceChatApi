using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20260328061000_SoftDeleteAndResponseArchive")]
public partial class SoftDeleteAndResponseArchive : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsDeleted",
            table: "Conversations",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DeletedAt",
            table: "Conversations",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Conversations_IsDeleted",
            table: "Conversations",
            column: "IsDeleted");

        migrationBuilder.CreateTable(
            name: "RequestResponseArchives",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserRequest = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RequestResponseArchives", x => x.Id);
                table.ForeignKey(
                    name: "FK_RequestResponseArchives_Conversations_ConversationId",
                    column: x => x.ConversationId,
                    principalTable: "Conversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RequestResponseArchives_ConversationId_CreatedAt",
            table: "RequestResponseArchives",
            columns: new[] { "ConversationId", "CreatedAt" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "RequestResponseArchives");

        migrationBuilder.DropIndex(
            name: "IX_Conversations_IsDeleted",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "DeletedAt",
            table: "Conversations");

        migrationBuilder.DropColumn(
            name: "IsDeleted",
            table: "Conversations");
    }
}
