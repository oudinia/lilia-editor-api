using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Data only: the catalog row for csquotes' <c>displayquote</c>, which the LaTeX parser now reads as a blockquote
    /// (KnownEnvironments). Mathpix writes it for a quoted passage; kept as raw LaTeX it stopped the compile
    /// ("Environment displayquote undefined", the launch bot's notebook G, 21 Nov 2026). Mirrors the 'quotation' row,
    /// with handler_kind 'known-structural' like the other KnownEnvironments members. Idempotent.
    /// </summary>
    public partial class CatalogDisplayquote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO latex_tokens (id, name, kind, package_slug, arity, optional_arity, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES (gen_random_uuid(), 'displayquote', 'environment', 'csquotes', 0, 2, true, 'quote', 'blockquote', 'full', 'known-structural',
        'csquotes display quotation → blockquote; its optional [citation][punctuation] are dropped')
ON CONFLICT DO NOTHING;
UPDATE latex_tokens SET handler_kind = 'known-structural'
 WHERE kind = 'environment' AND name = 'displayquote' AND handler_kind IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM latex_tokens WHERE kind = 'environment' AND name = 'displayquote' AND notes LIKE 'csquotes display quotation%';");
        }
    }
}
