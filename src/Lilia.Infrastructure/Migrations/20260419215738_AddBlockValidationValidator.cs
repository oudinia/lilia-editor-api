using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockValidationValidator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_block_validation_block_hash_version",
                table: "block_validations");

            migrationBuilder.AddColumn<string>(
                name: "validator",
                table: "block_validations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pdflatex");

            migrationBuilder.CreateIndex(
                name: "ux_block_validation_block_hash_validator_version",
                table: "block_validations",
                columns: new[] { "block_id", "content_hash", "validator", "rule_version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations",
                sql: "validator IN ('pdflatex','typst')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_block_validation_block_hash_validator_version",
                table: "block_validations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_block_validation_validator",
                table: "block_validations");

            migrationBuilder.DropColumn(
                name: "validator",
                table: "block_validations");

            migrationBuilder.CreateIndex(
                name: "ux_block_validation_block_hash_version",
                table: "block_validations",
                columns: new[] { "block_id", "content_hash", "rule_version" },
                unique: true);
        }
    }
}
