using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The bulk import timings and precision score (21 Nov 2026): nullable millisecond columns per stage on files and
    /// chunks, the background compile's result, each chunk's per-page precision (jsonb) and the file's, the cached
    /// reading's precision, and indexes on created_at for the stats window. Columns only: nothing is rewritten.
    /// </summary>
    public partial class BulkImportTimings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "precision_pages",
                table: "mathpix_chunk_cache",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compile_engine",
                table: "bulk_import_files",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compile_error",
                table: "bulk_import_files",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "compile_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "compile_ok",
                table: "bulk_import_files",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "first_page_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "low_precision_pages",
                table: "bulk_import_files",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "precision_score",
                table: "bulk_import_files",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "bulk_import_files",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "split_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "total_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "upload_ms",
                table: "bulk_import_files",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "mathpix_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "place_figures_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "place_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "place_parse_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "place_write_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "precision_pages",
                table: "bulk_import_chunks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "precision_score",
                table: "bulk_import_chunks",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "queue_wait_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tex_zip_ms",
                table: "bulk_import_chunks",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_files_created",
                table: "bulk_import_files",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_chunks_created",
                table: "bulk_import_chunks",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bulk_import_files_created",
                table: "bulk_import_files");

            migrationBuilder.DropIndex(
                name: "ix_bulk_import_chunks_created",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "precision_pages",
                table: "mathpix_chunk_cache");

            migrationBuilder.DropColumn(
                name: "compile_engine",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "compile_error",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "compile_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "compile_ok",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "first_page_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "low_precision_pages",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "precision_score",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "split_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "total_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "upload_ms",
                table: "bulk_import_files");

            migrationBuilder.DropColumn(
                name: "mathpix_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "place_figures_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "place_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "place_parse_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "place_write_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "precision_pages",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "precision_score",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "queue_wait_ms",
                table: "bulk_import_chunks");

            migrationBuilder.DropColumn(
                name: "tex_zip_ms",
                table: "bulk_import_chunks");
        }
    }
}
