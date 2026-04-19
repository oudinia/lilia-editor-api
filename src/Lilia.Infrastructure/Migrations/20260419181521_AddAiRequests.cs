using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "anthropic"),
                    model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    prompt_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    redaction_summary = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    completion_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_requests", x => x.id);
                    table.CheckConstraint("ck_ai_request_provider", "provider IN ('anthropic','openai','local')");
                    table.CheckConstraint("ck_ai_request_purpose", "purpose IN ('rephrase','summarise','suggest_headings','suggest_bibliography','fix_latex','expand_outline','review_finding','redact_pii','other')");
                    table.CheckConstraint("ck_ai_request_status", "status IN ('pending','success','error','rate_limited','redacted_refused')");
                    table.ForeignKey(
                        name: "FK_ai_requests_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_document",
                table: "ai_requests",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_user",
                table: "ai_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_user_status",
                table: "ai_requests",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_request_user_time",
                table: "ai_requests",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_requests");
        }
    }
}
