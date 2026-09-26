using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PerUserSnippetFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "snippet_favorites",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    snippet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snippet_favorites", x => new { x.user_id, x.snippet_id });
                    table.ForeignKey(
                        name: "FK_snippet_favorites_snippets_snippet_id",
                        column: x => x.snippet_id,
                        principalTable: "snippets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_snippet_favorites_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_snippet_favorites_snippet_id",
                table: "snippet_favorites",
                column: "snippet_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "snippet_favorites");
        }
    }
}
