using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenBlockValidationValidator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations",
                sql: "validator IN ('pdflatex','typst','lualatex','xelatex')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations",
                sql: "validator IN ('pdflatex','typst')");
        }
    }
}
