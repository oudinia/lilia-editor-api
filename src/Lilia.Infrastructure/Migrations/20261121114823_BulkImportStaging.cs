using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BulkImportStaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "promote_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "promoted_at",
                table: "bulk_import_files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "staged_macros",
                table: "bulk_import_files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "staged_preamble",
                table: "bulk_import_files",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "uses_staging",
                table: "bulk_import_files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                table: "bulk_import_chunks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bulk_import_staged_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_staged_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_import_staged_assets_bulk_import_files_file_id",
                        column: x => x.file_id,
                        principalTable: "bulk_import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bulk_import_staged_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    order_in_chunk = table.Column<int>(type: "integer", nullable: false),
                    block_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_staged_blocks", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_import_staged_blocks_bulk_import_files_file_id",
                        column: x => x.file_id,
                        principalTable: "bulk_import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_staged_assets_file_chunk",
                table: "bulk_import_staged_assets",
                columns: new[] { "file_id", "chunk_index" });

            migrationBuilder.CreateIndex(
                name: "ux_bulk_import_staged_assets_file_name",
                table: "bulk_import_staged_assets",
                columns: new[] { "file_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_staged_blocks_file_order",
                table: "bulk_import_staged_blocks",
                columns: new[] { "file_id", "chunk_index", "order_in_chunk" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_import_staged_assets");

            migrationBuilder.DropTable(
                name: "bulk_import_staged_blocks");

            migrationBuilder.DropColumn(
                name: "promote_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "promoted_at",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "staged_macros",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "staged_preamble",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "uses_staging",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "bulk_import_chunks");
        }
    }
}
