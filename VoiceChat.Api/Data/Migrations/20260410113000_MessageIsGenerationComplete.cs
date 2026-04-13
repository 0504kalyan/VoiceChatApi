using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VoiceChat.Api.Data;

#nullable disable

namespace VoiceChat.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260410113000_MessageIsGenerationComplete")]
public partial class MessageIsGenerationComplete : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsGenerationComplete",
            table: "Messages",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsGenerationComplete",
            table: "Messages");
    }
}
