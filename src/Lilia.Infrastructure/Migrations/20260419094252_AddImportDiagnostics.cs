using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_finalize_enabled",
                table: "import_review_sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "project_session_id",
                table: "import_review_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quality_score",
                table: "import_review_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_diagnostics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    element_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_line_start = table.Column<int>(type: "integer", nullable: true),
                    source_line_end = table.Column<int>(type: "integer", nullable: true),
                    source_col_start = table.Column<int>(type: "integer", nullable: true),
                    source_col_end = table.Column<int>(type: "integer", nullable: true),
                    source_snippet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "warning"),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    suggested_action = table.Column<string>(type: "text", nullable: true),
                    auto_fix_applied = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    docs_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    dismissed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    dismissed_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    dismissed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_diagnostics", x => x.id);
                    table.CheckConstraint("ck_import_diagnostic_category", "category IN ('unsupported_class','unsupported_package','load_order','unknown_macro','missing_asset','bibliography_unresolved','preamble_conflict','parse_ambiguity','auto_shimmed','size_truncated')");
                    table.CheckConstraint("ck_import_diagnostic_severity", "severity IN ('error','warning','info')");
                    table.ForeignKey(
                        name: "FK_import_diagnostics_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_block",
                table: "import_diagnostics",
                columns: new[] { "session_id", "block_id" });

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_code",
                table: "import_diagnostics",
                column: "code");

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_session",
                table: "import_diagnostics",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_severity_active",
                table: "import_diagnostics",
                columns: new[] { "session_id", "severity" },
                filter: "dismissed = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_diagnostics");

            migrationBuilder.DropColumn(
                name: "auto_finalize_enabled",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "project_session_id",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "quality_score",
                table: "import_review_sessions");
        }
    }
}
