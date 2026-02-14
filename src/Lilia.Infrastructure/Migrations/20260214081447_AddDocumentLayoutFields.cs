using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLayoutFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<JsonDocument>(
                name: "paragraph_traces",
                table: "import_review_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_file_path",
                table: "import_review_sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderText",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LineSpacing",
                table: "documents",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarginBottom",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarginLeft",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarginRight",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MarginTop",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PageNumbering",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParagraphIndent",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    details = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropColumn(
                name: "paragraph_traces",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "source_file_path",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "FooterText",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "HeaderText",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "LineSpacing",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "MarginBottom",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "MarginLeft",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "MarginRight",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "MarginTop",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "PageNumbering",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "ParagraphIndent",
                table: "documents");
        }
    }
}
