using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageRequiresEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "requires_engine",
                table: "latex_packages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Seed the three the regex already hard-codes, so the catalog and the
            // fallback agree from the first boot instead of disagreeing until
            // someone notices. Everything else stays null: most packages run
            // anywhere, and only the exceptions are worth a row.
            //
            // UPDATE, not INSERT — these packages are already in the catalog, and
            // one that isn't needs a full row, not an engine.
            migrationBuilder.Sql("""
                UPDATE latex_packages
                   SET requires_engine = 'lualatex', updated_at = NOW()
                 WHERE slug IN ('fontspec', 'unicode-math', 'polyglossia')
                   AND requires_engine IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_engine",
                table: "latex_packages");
        }
    }
}
