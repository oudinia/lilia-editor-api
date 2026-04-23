using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStage7And9Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_archive_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    source_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    document_class = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    final_state = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    total_blocks = table.Column<int>(type: "integer", nullable: false),
                    block_counts_by_type = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    error_count = table.Column<int>(type: "integer", nullable: false),
                    warning_count = table.Column<int>(type: "integer", nullable: false),
                    quality_score = table.Column<int>(type: "integer", nullable: true),
                    coverage_mapped_percent = table.Column<double>(type: "double precision", nullable: true),
                    unsupported_token_count = table.Column<int>(type: "integer", nullable: false),
                    instance_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    instance_last_activity_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    lifetime_minutes = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_archive_stats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rev_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    rev_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rev_assets", x => x.id);
                    table.ForeignKey(
                        name: "FK_rev_assets_rev_documents_rev_document_id",
                        column: x => x.rev_document_id,
                        principalTable: "rev_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rev_bibliography_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    rev_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cite_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    entry_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    data = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    formatted_text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rev_bibliography_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_rev_bibliography_entries_rev_documents_rev_document_id",
                        column: x => x.rev_document_id,
                        principalTable: "rev_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_archive_stats_archived_at",
                table: "import_archive_stats",
                column: "archived_at");

            migrationBuilder.CreateIndex(
                name: "IX_import_archive_stats_final_state",
                table: "import_archive_stats",
                column: "final_state");

            migrationBuilder.CreateIndex(
                name: "IX_import_archive_stats_owner_id",
                table: "import_archive_stats",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_archive_stats_source_format",
                table: "import_archive_stats",
                column: "source_format");

            migrationBuilder.CreateIndex(
                name: "IX_rev_assets_rev_document_id",
                table: "rev_assets",
                column: "rev_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_rev_bibliography_entries_rev_document_id",
                table: "rev_bibliography_entries",
                column: "rev_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_rev_bibliography_entries_rev_document_id_cite_key",
                table: "rev_bibliography_entries",
                columns: new[] { "rev_document_id", "cite_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_archive_stats");

            migrationBuilder.DropTable(
                name: "rev_assets");

            migrationBuilder.DropTable(
                name: "rev_bibliography_entries");
        }
    }
}
