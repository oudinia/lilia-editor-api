using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFormulaThemeAndTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "slug",
                table: "formulas",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "theme",
                table: "formulas",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tokens_json",
                table: "formulas",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_formulas_slug",
                table: "formulas",
                column: "slug",
                unique: true,
                filter: "\"slug\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_formulas_theme",
                table: "formulas",
                column: "theme");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_formulas_slug",
                table: "formulas");

            migrationBuilder.DropIndex(
                name: "IX_formulas_theme",
                table: "formulas");

            migrationBuilder.DropColumn(
                name: "slug",
                table: "formulas");

            migrationBuilder.DropColumn(
                name: "theme",
                table: "formulas");

            migrationBuilder.DropColumn(
                name: "tokens_json",
                table: "formulas");
        }
    }
}
