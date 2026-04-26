using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportTelemetryEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_telemetry_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    event_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "warn"),
                    source_format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_or_env = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    block_kind_emitted = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    block_kind_expected = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    import_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sample_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_telemetry_events", x => x.id);
                    table.CheckConstraint("ck_telemetry_event_kind", "event_kind IN ('unknown_env','unhandled_token','silent_fallback','cell_cleanup_applied','partial_parse','expected_leak_hit','cmd_passthrough','unsupported_block_emitted','parser_warning')");
                    table.CheckConstraint("ck_telemetry_severity", "severity IN ('info','warn','error')");
                    table.CheckConstraint("ck_telemetry_source_format", "source_format IN ('latex','docx','epub','pdf','lml','overleaf-zip')");
                    table.ForeignKey(
                        name: "FK_import_telemetry_events_import_review_sessions_import_sessi~",
                        column: x => x.import_session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_format_severity",
                table: "import_telemetry_events",
                columns: new[] { "source_format", "severity", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_kind_recent",
                table: "import_telemetry_events",
                columns: new[] { "event_kind", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_session",
                table: "import_telemetry_events",
                column: "import_session_id",
                filter: "import_session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_telemetry_token_recent",
                table: "import_telemetry_events",
                columns: new[] { "token_or_env", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_telemetry_events");
        }
    }
}
