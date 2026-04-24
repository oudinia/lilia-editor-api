using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Catches up the catalog with the 30 parser HashSet members that
    /// the Stage-3 boot-time audit (shipped 80550a1) reported as orphans
    /// — parser handles them, catalog never catalogued them.
    ///
    /// Relies on the NULLS NOT DISTINCT unique index (shipped with
    /// 20260424030048) so ON CONFLICT upserts land on the correct row
    /// when the kernel-scope NULL package_slug is the conflict target.
    ///
    /// Classification comes straight from the audit log output:
    ///
    ///   known-structural (env, 1) : algorithm2e
    ///   theorem-like (env, 9)     : prop, cor, conjecture, defn,
    ///                                example, remark, note, exercise,
    ///                                axiom
    ///   pass-through (env, 3)     : center, flushleft, flushright
    ///                                (these are in BOTH KnownEnvironments
    ///                                AND PassThroughEnvironments in the
    ///                                parser; pass-through handler runs
    ///                                first, so that's the primary claim)
    ///   inline-preserved (cmd, 5) : hyperref, footnotemark, footnotetext,
    ///                                input, include
    ///   inline-code (cmd, 9)      : inlinecode, code, cmdname, macroname,
    ///                                pkgname, filename, envname,
    ///                                lstinline, mintinline
    ///
    /// Total: 27 unique tokens (center/flushleft/flushright appeared in
    /// two audit categories; one row each covers both).
    /// </summary>
    public partial class CatalogCatchupForBootAuditOrphans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Environments ─────────────────────────────────────────────────────

-- known-structural (1)
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'algorithm2e', 'environment', NULL, true, 'layout', 'algorithm', 'full', 'known-structural', 'Algorithm-pseudocode env — same dispatch path as \\begin{algorithm}.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      handler_kind   = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      maps_to_block_type = EXCLUDED.maps_to_block_type,
      expects_body   = EXCLUDED.expects_body,
      notes          = EXCLUDED.notes,
      updated_at     = NOW();

-- theorem-like (9 extras beyond what Pass-3 already inserted)
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'prop',       'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like abbreviation mapped by TheoremEnvironments dict to Proposition.'),
  (gen_random_uuid(), 'cor',        'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like abbreviation mapped by TheoremEnvironments dict to Corollary.'),
  (gen_random_uuid(), 'conjecture', 'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Conjecture.'),
  (gen_random_uuid(), 'defn',       'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like abbreviation mapped by TheoremEnvironments dict to Definition.'),
  (gen_random_uuid(), 'example',    'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Example.'),
  (gen_random_uuid(), 'remark',     'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Remark.'),
  (gen_random_uuid(), 'note',       'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Note.'),
  (gen_random_uuid(), 'exercise',   'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Exercise.'),
  (gen_random_uuid(), 'axiom',      'environment', NULL, true, 'theorem', 'theorem', 'full', 'theorem-like', 'Theorem-like env mapped by TheoremEnvironments dict to Axiom.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      handler_kind   = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      maps_to_block_type = EXCLUDED.maps_to_block_type,
      expects_body   = EXCLUDED.expects_body,
      notes          = EXCLUDED.notes,
      updated_at     = NOW();

-- pass-through (3) — note: also in KnownEnvironments; pass-through
-- runs first so the primary handler_kind is pass-through.
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'center',     'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Alignment wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'flushleft',  'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Alignment wrapper dropped on import; body parses as usual.'),
  (gen_random_uuid(), 'flushright', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Alignment wrapper dropped on import; body parses as usual.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      handler_kind   = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      expects_body   = EXCLUDED.expects_body,
      notes          = EXCLUDED.notes,
      updated_at     = NOW();

-- Commands ─────────────────────────────────────────────────────────

-- inline-preserved (5)
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'hyperref',     'command', NULL, false, 'reference', NULL, 'full', 'inline-preserved', 'Preserved verbatim by NormaliseInlineCommands; downstream renderer resolves the link.'),
  (gen_random_uuid(), 'footnotemark', 'command', NULL, false, 'reference', NULL, 'full', 'inline-preserved', 'Preserved verbatim; footnote marker renders alongside the surrounding paragraph.'),
  (gen_random_uuid(), 'footnotetext', 'command', NULL, false, 'reference', NULL, 'full', 'inline-preserved', 'Preserved verbatim; footnote text pairs with a preceding \\footnotemark.'),
  (gen_random_uuid(), 'input',        'command', NULL, false, 'utility',   NULL, 'full', 'inline-preserved', 'Preserved verbatim; external-file includes are resolved by the author before import, not by the parser.'),
  (gen_random_uuid(), 'include',      'command', NULL, false, 'utility',   NULL, 'full', 'inline-preserved', 'Preserved verbatim; external-file includes are resolved by the author before import, not by the parser.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      handler_kind   = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      notes          = EXCLUDED.notes,
      updated_at     = NOW();

-- inline-code (9)
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'inlinecode',  'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'code',        'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'cmdname',     'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'macroname',   'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'pkgname',     'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'filename',    'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'envname',     'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'lstlinline',  'command', NULL, false, 'font', NULL, 'unsupported', NULL, 'Placeholder — typo guard. Real entry is lstinline.'),
  (gen_random_uuid(), 'lstinline',   'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.'),
  (gen_random_uuid(), 'mintinline',  'command', NULL, false, 'font', NULL, 'full', 'inline-code', 'CodeDisplayInlineCommands: argument wrapped in Markdown backticks at import time.')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      handler_kind   = EXCLUDED.handler_kind,
      semantic_category = EXCLUDED.semantic_category,
      notes          = EXCLUDED.notes,
      updated_at     = NOW();

-- Clean up the placeholder typo guard we just inserted (defensive;
-- lstlinline is not a real parser token). Keeps the catalog tidy.
DELETE FROM latex_tokens WHERE name = 'lstlinline' AND kind = 'command' AND package_slug IS NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. These rows describe real parser behavior; reversing
            // would re-introduce orphans.
        }
    }
}
