using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "definition_id",
                table: "import_review_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "import_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    owner_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "tex"),
                    raw_source = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_definitions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_review_sessions_definition_id",
                table: "import_review_sessions",
                column: "definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_definitions_created_at",
                table: "import_definitions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_import_definitions_owner_id",
                table: "import_definitions",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "FK_import_review_sessions_import_definitions_definition_id",
                table: "import_review_sessions",
                column: "definition_id",
                principalTable: "import_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Backfill: every existing session predates the control-plane
            // split, so it belongs to a one-off definition. Create that
            // definition on-the-fly and point the session at it. Idempotent
            // via the IS NULL guard so a re-run after partial success
            // doesn't duplicate.
            migrationBuilder.Sql(@"
WITH ins AS (
  INSERT INTO import_definitions (id, owner_id, source_file_name, source_format, raw_source, created_at)
  SELECT gen_random_uuid(),
         s.owner_id,
         s.document_title,
         s.source_format,
         s.raw_import_data,
         s.created_at
  FROM import_review_sessions s
  WHERE s.definition_id IS NULL
  RETURNING id, owner_id, created_at
)
UPDATE import_review_sessions s
SET    definition_id = ins.id
FROM   ins
WHERE  s.definition_id IS NULL
  AND  s.owner_id = ins.owner_id
  AND  s.created_at = ins.created_at;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_import_review_sessions_import_definitions_definition_id",
                table: "import_review_sessions");

            migrationBuilder.DropTable(
                name: "import_definitions");

            migrationBuilder.DropIndex(
                name: "IX_import_review_sessions_definition_id",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "definition_id",
                table: "import_review_sessions");
        }
    }
}
