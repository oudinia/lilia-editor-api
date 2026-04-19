using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLatexEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "latex_engine",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pdflatex");

            migrationBuilder.AddCheckConstraint(
                name: "ck_document_latex_engine",
                table: "documents",
                sql: "latex_engine IN ('pdflatex','xelatex','lualatex')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_document_latex_engine",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "latex_engine",
                table: "documents");
        }
    }
}
