using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHeaderFooterLCRSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "footer_center",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "footer_left",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "footer_right",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_center",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_left",
                table: "documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "header_right",
                table: "documents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "footer_center",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "footer_left",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "footer_right",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "header_center",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "header_left",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "header_right",
                table: "documents");
        }
    }
}
