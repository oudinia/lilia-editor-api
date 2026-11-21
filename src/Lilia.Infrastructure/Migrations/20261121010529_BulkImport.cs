using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BulkImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_import_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_batches", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_import_batches_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mathpix_chunk_cache",
                columns: table => new
                {
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    pages = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mathpix_chunk_cache", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "bulk_import_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    original_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pages = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preamble_applied = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    warnings = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_files", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_import_files_bulk_import_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "bulk_import_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bulk_import_files_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "bulk_import_chunks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    index = table.Column<int>(type: "integer", nullable: false),
                    first_page = table.Column<int>(type: "integer", nullable: false),
                    last_page = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    chunk_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    mathpix_pdf_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pages_completed = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    result_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    from_cache = table.Column<bool>(type: "boolean", nullable: false),
                    charged_pages = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    placeholder_block_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_import_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_bulk_import_chunks_bulk_import_files_file_id",
                        column: x => x.file_id,
                        principalTable: "bulk_import_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_batches_status",
                table: "bulk_import_batches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_batches_user",
                table: "bulk_import_batches",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_chunks_status",
                table: "bulk_import_chunks",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_chunks_user_status",
                table: "bulk_import_chunks",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_bulk_import_chunks_file_index",
                table: "bulk_import_chunks",
                columns: new[] { "file_id", "index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_files_batch",
                table: "bulk_import_files",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_files_document",
                table: "bulk_import_files",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_files_status",
                table: "bulk_import_files",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_import_files_user_status",
                table: "bulk_import_files",
                columns: new[] { "user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_import_chunks");

            migrationBuilder.DropTable(
                name: "mathpix_chunk_cache");

            migrationBuilder.DropTable(
                name: "bulk_import_files");

            migrationBuilder.DropTable(
                name: "bulk_import_batches");
        }
    }
}
