using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImportReviewRedesignColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastFocusedTab",
                table: "import_review_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFormat",
                table: "import_review_sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "TabProgress",
                table: "import_review_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceFile",
                table: "import_block_reviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "SourceRange",
                table: "import_block_reviews",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastFocusedTab",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "SourceFormat",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "TabProgress",
                table: "import_review_sessions");

            migrationBuilder.DropColumn(
                name: "SourceFile",
                table: "import_block_reviews");

            migrationBuilder.DropColumn(
                name: "SourceRange",
                table: "import_block_reviews");
        }
    }
}
