using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStructuralFindingSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "import_structural_findings",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "rule");

            migrationBuilder.AddCheckConstraint(
                name: "ck_structural_finding_source",
                table: "import_structural_findings",
                sql: "source IN ('rule','ai','manual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_structural_finding_source",
                table: "import_structural_findings");

            migrationBuilder.DropColumn(
                name: "source",
                table: "import_structural_findings");
        }
    }
}
