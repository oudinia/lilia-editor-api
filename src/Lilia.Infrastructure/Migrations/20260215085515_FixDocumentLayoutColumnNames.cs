using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixDocumentLayoutColumnNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ParagraphIndent",
                table: "documents",
                newName: "paragraph_indent");

            migrationBuilder.RenameColumn(
                name: "PageNumbering",
                table: "documents",
                newName: "page_numbering");

            migrationBuilder.RenameColumn(
                name: "MarginTop",
                table: "documents",
                newName: "margin_top");

            migrationBuilder.RenameColumn(
                name: "MarginRight",
                table: "documents",
                newName: "margin_right");

            migrationBuilder.RenameColumn(
                name: "MarginLeft",
                table: "documents",
                newName: "margin_left");

            migrationBuilder.RenameColumn(
                name: "MarginBottom",
                table: "documents",
                newName: "margin_bottom");

            migrationBuilder.RenameColumn(
                name: "LineSpacing",
                table: "documents",
                newName: "line_spacing");

            migrationBuilder.RenameColumn(
                name: "HeaderText",
                table: "documents",
                newName: "header_text");

            migrationBuilder.RenameColumn(
                name: "FooterText",
                table: "documents",
                newName: "footer_text");

            migrationBuilder.AlterColumn<string>(
                name: "page_numbering",
                table: "documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "paragraph_indent",
                table: "documents",
                newName: "ParagraphIndent");

            migrationBuilder.RenameColumn(
                name: "page_numbering",
                table: "documents",
                newName: "PageNumbering");

            migrationBuilder.RenameColumn(
                name: "margin_top",
                table: "documents",
                newName: "MarginTop");

            migrationBuilder.RenameColumn(
                name: "margin_right",
                table: "documents",
                newName: "MarginRight");

            migrationBuilder.RenameColumn(
                name: "margin_left",
                table: "documents",
                newName: "MarginLeft");

            migrationBuilder.RenameColumn(
                name: "margin_bottom",
                table: "documents",
                newName: "MarginBottom");

            migrationBuilder.RenameColumn(
                name: "line_spacing",
                table: "documents",
                newName: "LineSpacing");

            migrationBuilder.RenameColumn(
                name: "header_text",
                table: "documents",
                newName: "HeaderText");

            migrationBuilder.RenameColumn(
                name: "footer_text",
                table: "documents",
                newName: "FooterText");

            migrationBuilder.AlterColumn<string>(
                name: "PageNumbering",
                table: "documents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
