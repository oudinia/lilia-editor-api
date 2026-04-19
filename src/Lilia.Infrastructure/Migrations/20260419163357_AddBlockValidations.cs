using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "block_validations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "valid"),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    warnings = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    rule_version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "v1"),
                    validated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_validations", x => x.id);
                    table.CheckConstraint("ck_block_validation_status", "status IN ('valid','error','warning')");
                    table.ForeignKey(
                        name: "FK_block_validations_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_block_validations_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_block_validation_document",
                table: "block_validations",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_validation_document_status",
                table: "block_validations",
                columns: new[] { "document_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_block_validation_block_hash_version",
                table: "block_validations",
                columns: new[] { "block_id", "content_hash", "rule_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "block_validations");
        }
    }
}
