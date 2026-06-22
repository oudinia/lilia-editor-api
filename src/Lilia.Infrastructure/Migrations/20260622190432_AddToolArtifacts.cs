using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddToolArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tool_artifacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tool_slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    user_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    anon_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    input = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    output = table.Column<string>(type: "text", nullable: true),
                    output_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    output_bytes = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    truncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tool_artifacts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tool_artifact_created",
                table: "tool_artifacts",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_tool_artifact_slug_created",
                table: "tool_artifacts",
                columns: new[] { "tool_slug", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tool_artifacts");
        }
    }
}
