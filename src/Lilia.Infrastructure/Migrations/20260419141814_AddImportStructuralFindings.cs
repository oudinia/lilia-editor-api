using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportStructuralFindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "document_category",
                table: "import_review_sessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_category",
                table: "documents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_structural_findings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    block_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "hint"),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    detail = table.Column<string>(type: "text", nullable: false),
                    suggested_action = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    action_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action_payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    resolved_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_structural_findings", x => x.id);
                    table.CheckConstraint("ck_structural_finding_action", "action_kind IN ('convert_block_type','set_document_class','delete_block','split_header_table','open_edit_modal','merge_list')");
                    table.CheckConstraint("ck_structural_finding_kind", "kind IN ('cv_section','paragraph_is_cv_section','paragraph_as_heading','personal_info','header_table_unpack','spurious_toc','cv_class_suggestion','cv_list_style','fragmented_list','layout_table','missing_figure_caption','orphan_subheading_chain')");
                    table.CheckConstraint("ck_structural_finding_owner", "(session_id IS NOT NULL AND document_id IS NULL) OR (session_id IS NULL AND document_id IS NOT NULL)");
                    table.CheckConstraint("ck_structural_finding_severity", "severity IN ('hint','warning','critical')");
                    table.CheckConstraint("ck_structural_finding_status", "status IN ('pending','applied','dismissed')");
                    table.ForeignKey(
                        name: "FK_import_structural_findings_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_import_structural_findings_import_review_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_structural_finding_document",
                table: "import_structural_findings",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_structural_finding_document_status",
                table: "import_structural_findings",
                columns: new[] { "document_id", "status" },
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_structural_finding_kind",
                table: "import_structural_findings",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_structural_finding_session",
                table: "import_structural_findings",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_structural_finding_session_status",
                table: "import_structural_findings",
                columns: new[] { "session_id", "status" },
                filter: "status = 'pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_structural_findings");

            migrationBuilder.DropColumn(
                name: "document_category",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "document_category",
                table: "documents");
        }
    }
}
