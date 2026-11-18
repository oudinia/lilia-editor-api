using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Catalog rows for the citation commands the LaTeX importer now keeps in text (launch-bot #231, 18 Nov 2026):
    /// REVTeX's \onlinecite and natbib's \citenum, \citealp and \citealt were flattened, so the key printed and the
    /// citation was lost. CatalogIntegrityTests wants every member of the parser's preserved set in the catalog.
    /// A row is written where it is missing, or where it is still the parser's placeholder (as the 17 Oct backfill does).
    /// </summary>
    public partial class CatalogCitationCommands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TEMP TABLE catalog_citations (
  name varchar, kind varchar, arity int, optional_arity int, expects_body boolean,
  semantic_category varchar, maps_to_block_type varchar, coverage_level varchar,
  notes text, handler_kind varchar, insert_template text
) ON COMMIT DROP;

INSERT INTO catalog_citations VALUES
  ('onlinecite', 'command', 1, 0, false, 'reference', NULL, 'full', 'Preserved verbatim at import (REVTeX''s citation without brackets); a \providecommand{\onlinecite}{\cite} covers the classes that lack it.', 'inline-preserved', '\onlinecite{|CURSOR|}'),
  ('citenum', 'command', 1, 0, false, 'reference', NULL, 'full', 'Preserved verbatim at import (natbib''s citation number).', 'inline-preserved', '\citenum{|CURSOR|}'),
  ('citealp', 'command', 1, 2, false, 'reference', NULL, 'full', 'Preserved verbatim at import (natbib''s citation without parentheses).', 'inline-preserved', '\citealp{|CURSOR|}'),
  ('citealt', 'command', 1, 2, false, 'reference', NULL, 'full', 'Preserved verbatim at import (natbib''s textual citation without parentheses).', 'inline-preserved', '\citealt{|CURSOR|}');

UPDATE latex_tokens t
   SET arity = b.arity, optional_arity = b.optional_arity, expects_body = b.expects_body,
       semantic_category = b.semantic_category, maps_to_block_type = b.maps_to_block_type,
       coverage_level = b.coverage_level, notes = b.notes, handler_kind = b.handler_kind,
       insert_template = b.insert_template, updated_at = NOW()
  FROM catalog_citations b
 WHERE t.name = b.name AND t.kind = b.kind AND t.package_slug IS NULL
   AND t.coverage_level = 'unsupported'
   AND t.notes LIKE 'Auto-inserted by parser%'
   AND t.handler_kind IS NULL;

INSERT INTO latex_tokens (id, name, kind, package_slug, arity, optional_arity, expects_body,
                          semantic_category, maps_to_block_type, coverage_level, notes,
                          handler_kind, insert_template, created_at, updated_at)
SELECT gen_random_uuid(), b.name, b.kind, NULL, b.arity, b.optional_arity, b.expects_body,
       b.semantic_category, b.maps_to_block_type, b.coverage_level, b.notes,
       b.handler_kind, b.insert_template, NOW(), NOW()
  FROM catalog_citations b
 WHERE NOT EXISTS (SELECT 1 FROM latex_tokens t
                    WHERE t.name = b.name AND t.kind = b.kind AND t.package_slug IS NULL);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: removing the rows would only make the parser insert placeholders again.
        }
    }
}
