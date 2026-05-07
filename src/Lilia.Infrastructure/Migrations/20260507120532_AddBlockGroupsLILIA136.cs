using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockGroupsLILIA136 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "block_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dimension = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    attributes = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_block_groups_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "block_group_memberships",
                columns: table => new
                {
                    block_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_group_memberships", x => new { x.block_id, x.group_id });
                    table.ForeignKey(
                        name: "FK_block_group_memberships_block_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "block_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_block_group_memberships_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_block_group_member_block",
                table: "block_group_memberships",
                column: "block_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_group_member_group",
                table: "block_group_memberships",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_group_document",
                table: "block_groups",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_block_group_document_dimension",
                table: "block_groups",
                columns: new[] { "document_id", "dimension" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "block_group_memberships");

            migrationBuilder.DropTable(
                name: "block_groups");
        }
    }
}
