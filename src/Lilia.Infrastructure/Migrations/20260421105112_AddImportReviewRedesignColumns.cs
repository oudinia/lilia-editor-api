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
            // Column names are snake_case to match the rest of the
            // import_review_sessions / import_block_reviews schema. The
            // entity configuration maps PascalCase properties via
            // HasColumnName, so both EF queries and raw SQL (tab-progress
            // jsonb_set) converge on the same identifiers.
            migrationBuilder.AddColumn<string>(
                name: "last_focused_tab",
                table: "import_review_sessions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_format",
                table: "import_review_sessions",
                type: "text",
                nullable: false,
                defaultValue: "tex");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "tab_progress",
                table: "import_review_sessions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_file",
                table: "import_block_reviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "source_range",
                table: "import_block_reviews",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "last_focused_tab", table: "import_review_sessions");
            migrationBuilder.DropColumn(name: "source_format",    table: "import_review_sessions");
            migrationBuilder.DropColumn(name: "tab_progress",     table: "import_review_sessions");
            migrationBuilder.DropColumn(name: "source_file",      table: "import_block_reviews");
            migrationBuilder.DropColumn(name: "source_range",     table: "import_block_reviews");
        }
    }
}
