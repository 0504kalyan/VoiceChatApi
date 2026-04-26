using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceChat.Api.Data.Migrations
{
    /// <inheritdoc />
    [Migration("20260426094500_AddIdeNormalizationAndUserFk")]
    public partial class AddIdeNormalizationAndUserFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ide_files_workspace_id_path",
                table: "ide_files");

            migrationBuilder.AddColumn<string>(
                name: "normalized_name",
                table: "ide_workspaces",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "normalized_path",
                table: "ide_files",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "user_id",
                table: "ide_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE ide_workspaces
                SET normalized_name = upper(trim(regexp_replace(name, '\s+', ' ', 'g')))
                WHERE normalized_name = '';
                """);

            migrationBuilder.Sql(
                """
                UPDATE ide_files AS f
                SET
                    user_id = w.user_id,
                    normalized_path = upper(trim(both '/' from replace(f.path, '\', '/')))
                FROM ide_workspaces AS w
                WHERE f.workspace_id = w.id;
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "ide_workspaces",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "ide_workspaces",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<Guid>(
                name: "user_id",
                table: "ide_files",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "ide_files",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "ide_files",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.CreateIndex(
                name: "ix_ide_files_user_id_workspace_id_normalized_path",
                table: "ide_files",
                columns: new[] { "user_id", "workspace_id", "normalized_path" },
                unique: true,
                filter: "\"is_active\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "ix_ide_files_workspace_id_updated_at",
                table: "ide_files",
                columns: new[] { "workspace_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ide_workspaces_user_id_normalized_name",
                table: "ide_workspaces",
                columns: new[] { "user_id", "normalized_name" });

            migrationBuilder.AddForeignKey(
                name: "fk_ide_files_users_user_id",
                table: "ide_files",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_ide_files_users_user_id",
                table: "ide_files");

            migrationBuilder.DropIndex(
                name: "ix_ide_files_user_id_workspace_id_normalized_path",
                table: "ide_files");

            migrationBuilder.DropIndex(
                name: "ix_ide_files_workspace_id_updated_at",
                table: "ide_files");

            migrationBuilder.DropIndex(
                name: "ix_ide_workspaces_user_id_normalized_name",
                table: "ide_workspaces");

            migrationBuilder.DropColumn(
                name: "normalized_name",
                table: "ide_workspaces");

            migrationBuilder.DropColumn(
                name: "normalized_path",
                table: "ide_files");

            migrationBuilder.DropColumn(
                name: "user_id",
                table: "ide_files");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "ide_workspaces",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "ide_workspaces",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "ide_files",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "created_at",
                table: "ide_files",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "ix_ide_files_workspace_id_path",
                table: "ide_files",
                columns: new[] { "workspace_id", "path" },
                unique: true,
                filter: "\"is_active\" = TRUE");
        }
    }
}
