using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowSyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "sync_telemetry_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    event_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "warn"),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "server"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: true),
                    duration_ms = table.Column<int>(type: "integer", nullable: true),
                    detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_telemetry_events", x => x.id);
                    table.CheckConstraint("ck_sync_telemetry_event_kind", "event_kind IN ('conflict','sync_error','retry_exhausted','offline_span')");
                    table.CheckConstraint("ck_sync_telemetry_severity", "severity IN ('info','warn','error')");
                    table.CheckConstraint("ck_sync_telemetry_source", "source IN ('server','client')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_telemetry_document_recent",
                table: "sync_telemetry_events",
                columns: new[] { "document_id", "created_at" },
                descending: new[] { false, true },
                filter: "document_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sync_telemetry_kind_recent",
                table: "sync_telemetry_events",
                columns: new[] { "event_kind", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_telemetry_events");

            migrationBuilder.DropColumn(
                name: "version",
                table: "documents");
        }
    }
}
