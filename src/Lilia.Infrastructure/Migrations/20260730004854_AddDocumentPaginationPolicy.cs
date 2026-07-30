using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentPaginationPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pagination_policy",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_pagination_policy",
                table: "documents",
                sql: "pagination_policy IS NULL OR pagination_policy IN ('ragged','flush')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_pagination_policy",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "pagination_policy",
                table: "documents");
        }
    }
}
