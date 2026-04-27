using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceChat.Api.Data.Migrations
{
    /// <inheritdoc />
    [Migration("20260426090000_AddIdeWorkspaces")]
    public partial class AddIdeWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ide_workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ide_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "fk_ide_workspaces_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ide_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    language = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ide_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_ide_files_ide_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "ide_workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ide_files_workspace_id_path",
                table: "ide_files",
                columns: new[] { "workspace_id", "path" },
                unique: true,
                filter: "\"is_active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_ide_workspaces_user_id_updated_at",
                table: "ide_workspaces",
                columns: new[] { "user_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ide_files");

            migrationBuilder.DropTable(
                name: "ide_workspaces");
        }
    }
}
