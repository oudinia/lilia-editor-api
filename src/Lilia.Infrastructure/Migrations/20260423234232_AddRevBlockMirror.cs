using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRevBlockMirror : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rev_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    source_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rev_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_rev_documents_import_review_sessions_instance_id",
                        column: x => x.instance_id,
                        principalTable: "import_review_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rev_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    rev_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    content = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    path = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "kept"),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    warnings = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rev_blocks", x => x.id);
                    table.ForeignKey(
                        name: "FK_rev_blocks_rev_blocks_parent_id",
                        column: x => x.parent_id,
                        principalTable: "rev_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rev_blocks_rev_documents_rev_document_id",
                        column: x => x.rev_document_id,
                        principalTable: "rev_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rev_blocks_parent_id",
                table: "rev_blocks",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_rev_blocks_rev_document_id",
                table: "rev_blocks",
                column: "rev_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_rev_blocks_rev_document_id_sort_order",
                table: "rev_blocks",
                columns: new[] { "rev_document_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_rev_documents_instance_id",
                table: "rev_documents",
                column: "instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rev_blocks");

            migrationBuilder.DropTable(
                name: "rev_documents");
        }
    }
}
