using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Two classes of fix, both surfaced by the Stage-3 parallel-run drift
    /// check shipped in commits aaacd26 / 574e72a
    /// (lilia-docs/technical/latex-coverage-architecture.md).
    ///
    /// A — Delete kernel-scope pollution dupes of real package rows.
    /// The scanner's auto-insert path writes new tokens at package_slug
    /// NULL when it can't attribute them. Four such rows shadow an
    /// existing package-scoped row already at full/partial/shimmed:
    ///
    ///   theorem@kernel unsupported  ← dupes amsthm/full
    ///   appendices@kernel unsupported ← dupes appendix/partial
    ///   block@kernel unsupported      ← dupes beamer/partial
    ///   printbibliography@kernel unsupported ← dupes biblatex/partial
    ///
    /// Router lookups with packageSlug=null hit the kernel row first and
    /// return null (unsupported => no handler_kind). Deleting the dupes
    /// lets LookupToken fall through to the package-scoped row.
    ///
    /// B — Catalogue the parser's PassThroughEnvironments set.
    /// LatexParser recognises 16 spacing / alignment / font-size envs
    /// and unwraps their bodies. The catalog only had `spacing` at
    /// unsupported and was missing every other entry entirely.
    /// Promoted to coverage_level='partial' with
    /// handler_kind='pass-through' to match the parser.
    ///
    /// Idempotent: guarded by current state so re-runs are no-ops.
    /// </summary>
    public partial class CatalogDriftFixesFromStage3Probe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- A. Delete kernel-scope dupes that shadow a real package-scoped row.
-- Guarded by the existence of the package row at a real coverage level,
-- so we never delete a kernel row that's the only catalog entry.
DELETE FROM latex_tokens k
 USING latex_tokens p
 WHERE k.package_slug IS NULL
   AND k.coverage_level = 'unsupported'
   AND p.name = k.name
   AND p.kind = k.kind
   AND p.package_slug IS NOT NULL
   AND p.coverage_level IN ('full','partial','shimmed');

-- B. Catalogue the 16 pass-through envs from LatexParser.
-- Most are missing entirely; 'spacing' exists at unsupported and gets
-- promoted in-place via ON CONFLICT. coverage_level='partial' because
-- the wrapper is dropped (typography intent not preserved) but the
-- body parses as usual.
INSERT INTO latex_tokens
  (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'spacing',      'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Wrapper dropped on import; body parses as usual. Line-spacing intent is not preserved in the Lilia block model.'),
  (gen_random_uuid(), 'singlespace',  'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'doublespace',  'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'onehalfspace', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'raggedright',  'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Alignment wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'raggedleft',   'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Alignment wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'small',        'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'footnotesize', 'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'scriptsize',   'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'tiny',         'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'large',        'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'Large',        'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'LARGE',        'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'huge',         'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'Huge',         'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'normalsize',   'environment', NULL, true, 'font',   NULL, 'partial', 'pass-through', 'Font-size wrapper dropped on import; body parses as usual.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level    = EXCLUDED.coverage_level,
      handler_kind      = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      expects_body      = EXCLUDED.expects_body,
      notes             = EXCLUDED.notes,
      updated_at        = NOW()
  WHERE latex_tokens.coverage_level = 'unsupported';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. The deleted pollution dupes are auto-inserted by the
            // scanner if they reappear; the pass-through rows can stay
            // even if the migration is rolled back (they describe real
            // parser behaviour that won't change with a Down migration).
        }
    }
}
