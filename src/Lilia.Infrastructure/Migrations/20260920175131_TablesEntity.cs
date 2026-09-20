using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TablesEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Not part of this change. `documents.current_version_id` is model
            // drift that accumulated while the EF drift check was failing for
            // other reasons — the column already exists in every database that
            // ran database/027_seed_e2e_scenarios_v1.sql, which UPDATEs it, so a
            // plain AddColumn would fail at boot with "column already exists".
            //
            // Guarded so the snapshot and the schema agree without breaking an
            // existing database, following the precedent in
            // 20260422084705_ReconcileImportReviewColumnMappings.
            migrationBuilder.Sql(@"
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_attribute
    WHERE attrelid = 'public.documents'::regclass AND attname = 'current_version_id' AND NOT attisdropped
  ) THEN
    ALTER TABLE public.documents ADD COLUMN current_version_id uuid NULL;
  END IF;
END $$;");

            migrationBuilder.CreateTable(
                name: "tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    caption = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_tables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    block_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_tables", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_tables_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_document_tables_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "table_collaborators",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invited_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_collaborators", x => x.id);
                    table.ForeignKey(
                        name: "FK_table_collaborators_tables_table_id",
                        column: x => x.table_id,
                        principalTable: "tables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_tables_document_id_table_id",
                table: "document_tables",
                columns: new[] { "document_id", "table_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_tables_table_id",
                table: "document_tables",
                column: "table_id");

            migrationBuilder.CreateIndex(
                name: "IX_table_collaborators_table_id_user_id",
                table: "table_collaborators",
                columns: new[] { "table_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tables_owner_id_updated_at",
                table: "tables",
                columns: new[] { "owner_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_tables");

            migrationBuilder.DropTable(
                name: "table_collaborators");

            migrationBuilder.DropTable(
                name: "tables");

            // Deliberately not dropped on Down: the column predates this
            // migration in every real database, and removing it would take data
            // this migration never added.
        }
    }
}
