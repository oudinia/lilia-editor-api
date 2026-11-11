using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Operations weighted by complexity (the owner, 11 Nov 2026: "categorize them… an import of a document of many pages
    /// does not count the same as asking for a title… kind of like agility"). The catalogue of kinds with their points
    /// (story points: 0, 1, 2, 3, 5, 8, 13; an import also per 10 pages), adjustable from the admin console; each
    /// operation keeps the points it was given. Yesterday's export rows take the new codes and points.
    /// </summary>
    public partial class OperationPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_operation_kind",
                table: "operations");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "operations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<int>(
                name: "points",
                table: "operations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "units",
                table: "operations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "operation_kinds",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    points_per_unit = table.Column<int>(type: "integer", nullable: true),
                    unit_size = table.Column<int>(type: "integer", nullable: true),
                    unit_name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_kinds", x => x.code);
                    table.CheckConstraint("ck_operation_kind_category", "category IN ('help','read','edit','document','check','export','import','ai')");
                    table.CheckConstraint("ck_operation_kind_points", "points >= 0 AND (points_per_unit IS NULL OR (points_per_unit >= 0 AND unit_size > 0))");
                });

            migrationBuilder.CreateIndex(
                name: "ix_operations_kind_time",
                table: "operations",
                columns: new[] { "kind", "created_at" });

            migrationBuilder.Sql(@"
INSERT INTO operation_kinds (code, category, label, points, points_per_unit, unit_size, unit_name) VALUES
 ('help',                  'help',     'Read Lilia''s help',                       0, NULL, NULL, NULL),
 ('read',                  'read',     'Read a document (outline, block, search, settings, LaTeX)', 1, NULL, NULL, NULL),
 ('edit.small',            'edit',     'A small edit (a block, the title, page setup, the venue)', 1, NULL, NULL, NULL),
 ('document.create',       'document', 'Create an empty document',                 1, NULL, NULL, NULL),
 ('document.write',        'document', 'Write or rewrite a whole document',        3, NULL, NULL, NULL),
 ('check',                 'check',    'Check that a document compiles',           3, NULL, NULL, NULL),
 ('export.file',           'export',   'Export LaTeX, an Overleaf project, Word, Markdown or HTML', 2, NULL, NULL, NULL),
 ('export.pdf',            'export',   'Export a PDF',                             5, NULL, NULL, NULL),
 ('export.accessible_pdf', 'export',   'Export an accessible (tagged) PDF',        13, NULL, NULL, NULL),
 ('import',                'import',   'Import a file',                            5, 1, 10, 'pages');
UPDATE operations SET kind = CASE
    WHEN format = 'pdf' THEN 'export.pdf'
    ELSE 'export.file' END
 WHERE kind = 'export';
UPDATE operations o SET points = k.points FROM operation_kinds k WHERE k.code = o.kind;
");

            migrationBuilder.AddForeignKey(
                name: "FK_operations_operation_kinds_kind",
                table: "operations",
                column: "kind",
                principalTable: "operation_kinds",
                principalColumn: "code",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_operations_operation_kinds_kind",
                table: "operations");

            migrationBuilder.Sql("DELETE FROM operations WHERE kind NOT LIKE 'export.%'; UPDATE operations SET kind = 'export';");

            migrationBuilder.DropTable(
                name: "operation_kinds");

            migrationBuilder.DropIndex(
                name: "ix_operations_kind_time",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "points",
                table: "operations");

            migrationBuilder.DropColumn(
                name: "units",
                table: "operations");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "operations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AddCheckConstraint(
                name: "ck_operation_kind",
                table: "operations",
                sql: "kind IN ('export')");
        }
    }
}
