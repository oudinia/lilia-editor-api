using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// The CI `Parser_HashSets_have_no_catalog_orphans` test runs the
    /// boot audit against a fresh Testcontainers database. Earlier
    /// promotion migrations (CoverageHonestyPass2 / Pass3 / Pass4) used
    /// UPDATE-only SQL that required the row to already exist at
    /// coverage_level='unsupported' — prod had those rows because the
    /// scanner auto-inserted them; a fresh DB has no such seed row and
    /// Pass 2-4 no-oped.
    ///
    /// This migration upserts the 10 rows the Stage-3 boot audit
    /// flagged as missing on a fresh Testcontainers DB. In prod the
    /// rows already exist with the same values, so this is a no-op;
    /// on fresh DB it fills the seed gap so the integrity test sees
    /// the same state prod sees.
    ///
    /// Going forward, the pattern for promotion migrations is INSERT
    /// ... ON CONFLICT (not UPDATE-only) so fresh-DB CI and prod
    /// agree automatically. `CoverageHonestyPass*` are left alone as
    /// history; this migration is the catch-up.
    /// </summary>
    public partial class SeedGapRowsFromIntegrityTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- environments missing at kernel scope on fresh DB
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'eqnarray',   'environment', NULL, true, 'math',      'equation',  'full', 'known-structural', 'Legacy multi-line equation env; same dispatch as equation/align.'),
  (gen_random_uuid(), 'subfigure',  'environment', NULL, true, 'graphics',  'figure',    'full', 'known-structural', 'Sub-figure env inside a figure float; recognised by the parser figure handler.'),
  (gen_random_uuid(), 'algorithm',  'environment', NULL, true, 'layout',    'algorithm', 'full', 'known-structural', 'Algorithm float — parser emits ImportAlgorithm with typed pseudocode lines.'),
  (gen_random_uuid(), 'algorithmic','environment', NULL, true, 'layout',    'algorithm', 'full', 'known-structural', 'Inner algorithmic env — typed lines parsed by ParseAlgorithmicLines.'),
  (gen_random_uuid(), 'thm',        'environment', NULL, true, 'theorem',   'theorem',   'full', 'theorem-like',     'Theorem-like abbreviation mapped by TheoremEnvironments dict to Theorem.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level     = EXCLUDED.coverage_level,
      handler_kind       = EXCLUDED.handler_kind,
      semantic_category  = EXCLUDED.semantic_category,
      maps_to_block_type = EXCLUDED.maps_to_block_type,
      expects_body       = EXCLUDED.expects_body,
      notes              = COALESCE(latex_tokens.notes, EXCLUDED.notes),
      updated_at         = NOW();

-- commands missing at kernel scope on fresh DB
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'citeauthor', 'command', NULL, false, 'citation',  NULL, 'full', 'inline-preserved', 'Preserved verbatim by NormaliseInlineCommands; renders the author name from the bibliography.'),
  (gen_random_uuid(), 'citeyear',   'command', NULL, false, 'citation',  NULL, 'full', 'inline-preserved', 'Preserved verbatim by NormaliseInlineCommands; renders the publication year.'),
  (gen_random_uuid(), 'nocite',     'command', NULL, false, 'citation',  NULL, 'full', 'inline-preserved', 'Preserved verbatim; nocite keys still appear in the bibliography but do not emit an inline cite.'),
  (gen_random_uuid(), 'Cref',       'command', NULL, false, 'reference', NULL, 'full', 'inline-preserved', 'cleveref capitalised cross-reference — preserved verbatim; downstream resolves.'),
  (gen_random_uuid(), 'path',       'command', NULL, false, 'font',      NULL, 'full', 'inline-code',      'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level    = EXCLUDED.coverage_level,
      handler_kind      = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      notes             = COALESCE(latex_tokens.notes, EXCLUDED.notes),
      updated_at        = NOW();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op — reversing would reintroduce the orphan gap.
        }
    }
}
