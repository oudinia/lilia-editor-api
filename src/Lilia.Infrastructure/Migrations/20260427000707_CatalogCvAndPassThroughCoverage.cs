using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Catalog catch-up after the FT-TELEMETRY-001 fixture flood:
    ///
    ///   - Bumps CV macros (`\cvitem`, `\cvevent`, `\cvdegree`, …) and
    ///     the CV envs (`cvtable`, `twenty`, `subs`) from `none` /
    ///     `shimmed` to `full` — they're now unwrapped to markdown by
    ///     NormaliseCvMacros.
    ///   - Adds `divider`, `switchcolumn`, `columnbreak` as commands
    ///     stripped during normalisation (`full`).
    ///   - Adds layout pass-through envs (`minipage`, `multicols`,
    ///     `paracol`, `tcolorbox`, `frontmatter`, `letter`,
    ///     `IEEEkeywords`, `acronym`, `CJK`, `CJK*`) as `full` —
    ///     PassThroughEnvironments handles their bodies.
    ///   - Adds `longtable`, `wrapfigure` as `full` — dedicated handlers
    ///     in LatexParser route them to `tabular` / `figure`.
    ///   - Adds `bare-tabular` shape note for the SG-117 fix.
    ///
    /// All inserts use `ON CONFLICT … DO UPDATE` so re-running on a
    /// catalog that already has these rows just refreshes the coverage
    /// columns.
    /// </summary>
    public partial class CatalogCvAndPassThroughCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Packages FIRST (FK constraint on latex_tokens.package_slug).
INSERT INTO latex_packages (slug, display_name, category, coverage_level, coverage_notes, ctan_url) VALUES
  ('moderncv',          'moderncv',           'cv',     'full',    'CV macros expanded to markdown by NormaliseCvMacros', 'https://ctan.org/pkg/moderncv'),
  ('twentysecondscv',   'twentysecondscv',    'cv',     'full',    '\twentyitem expanded to markdown', 'https://ctan.org/pkg/twentysecondscv'),
  ('paracol',           'paracol',            'layout', 'full',    'Pass-through env; \switchcolumn stripped', 'https://ctan.org/pkg/paracol'),
  ('multicol',          'multicol',           'layout', 'full',    'Pass-through env; \columnbreak stripped', 'https://ctan.org/pkg/multicol'),
  ('tcolorbox',         'tcolorbox',          'layout', 'full',    'Pass-through env', 'https://ctan.org/pkg/tcolorbox'),
  ('longtable',         'longtable',          'table',  'full',    'Routed to ParseTabular', 'https://ctan.org/pkg/longtable'),
  ('wrapfig',           'wrapfig',            'graphics', 'full',    'Routed to figure handler', 'https://ctan.org/pkg/wrapfig'),
  ('CJK',               'CJK',                'language', 'full',    'Wrapper passed through', 'https://ctan.org/pkg/CJK'),
  ('IEEEtran',          'IEEEtran',           'utility',  'partial', '\IEEEkeywords env handled; full IEEE class still maps to article', 'https://ctan.org/pkg/IEEEtran')
ON CONFLICT (slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      coverage_notes = EXCLUDED.coverage_notes;

-- CV-template macros — now expanded to markdown by NormaliseCvMacros.
INSERT INTO latex_tokens (name, kind, package_slug, arity, optional_arity, expects_body, semantic_category, maps_to_block_type, coverage_level, notes) VALUES
  ('cvitem',             'command', 'moderncv', 4, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **title** — *org* — date — detail'),
  ('cvevent',            'command', 'moderncv', 4, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **title** — *org* — date — detail'),
  ('cvdegree',           'command', 'moderncv', 4, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **degree** in **field** — year — location'),
  ('cvitemshort',        'command', 'moderncv', 2, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **key** — value'),
  ('cvitemwithcomment',  'command', 'moderncv', 3, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **a** — b — c'),
  ('cvpubitem',          'command', 'moderncv', 3, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **title** by *authors* — venue'),
  ('cvuniversity',       'command', 'moderncv', 2, 0, false, 'cv', 'cvEntry', 'full', 'Unwrapped to markdown: **a** — b'),
  ('cvlistitem',         'command', 'moderncv', 2, 0, false, 'cv', 'list',    'full', 'Unwrapped to markdown list item'),
  ('twentyitem',         'command', 'twentysecondscv', 4, 0, false, 'cv', 'cvEntry', 'full', 'Twentysecondscv variant of cvevent'),
  -- CV envs.
  ('cvtable',            'environment', 'moderncv', 0, 1, true,  'cv', 'list', 'full', 'Pass-through wrapper — body is \cvitem entries'),
  ('twenty',             'environment', 'twentysecondscv', 0, 0, true, 'cv', 'list', 'full', 'Pass-through wrapper'),
  ('subs',               'environment', NULL,        0, 0, true, 'cv', NULL,    'full', 'Subsection wrapper — pass-through'),
  -- Standalone separator commands (NormaliseCvMacros strips with whitespace).
  ('divider',            'command', NULL, 0, 0, false, 'layout', NULL, 'full', 'Stripped — layout-only separator'),
  ('switchcolumn',       'command', 'paracol', 0, 0, false, 'layout', NULL, 'full', 'Stripped — paracol column switch'),
  ('columnbreak',        'command', 'multicol', 0, 0, false, 'layout', NULL, 'full', 'Stripped — column break'),
  ('sepspace',           'command', NULL, 0, 0, false, 'layout', NULL, 'full', 'Stripped — vertical space'),
  ('hbadness',           'command', NULL, 1, 0, false, 'layout', NULL, 'full', 'Stripped — line-break tolerance'),
  -- Layout / wrapper envs handled via PassThroughEnvironments.
  ('minipage',           'environment', NULL, 1, 1, true, 'layout', NULL, 'full', 'Pass-through — inner content parsed in document scope'),
  ('multicols',          'environment', 'multicol', 1, 0, true, 'layout', NULL, 'full', 'Pass-through'),
  ('paracol',            'environment', 'paracol', 1, 0, true, 'layout', NULL, 'full', 'Pass-through'),
  ('tcolorbox',          'environment', 'tcolorbox', 0, 1, true, 'layout', NULL, 'full', 'Pass-through'),
  ('frontmatter',        'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through — Elsevier elsarticle wrapper'),
  ('mainmatter',         'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through'),
  ('backmatter',         'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through'),
  ('letter',             'environment', NULL, 1, 0, true, 'structure', NULL, 'full', 'Pass-through — letter document class'),
  ('IEEEkeywords',       'environment', 'IEEEtran', 0, 0, true, 'structure', NULL, 'full', 'Pass-through — keyword list'),
  ('keywords',           'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through'),
  ('acronym',            'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through'),
  ('acronyms',           'environment', NULL, 0, 0, true, 'structure', NULL, 'full', 'Pass-through'),
  ('CJK',                'environment', 'CJK', 2, 0, true, 'structure', NULL, 'full', 'Pass-through — CJK script wrapper'),
  ('CJK*',               'environment', 'CJK', 2, 0, true, 'structure', NULL, 'full', 'Pass-through — CJK starred variant'),
  -- New table / figure handlers.
  ('longtable',          'environment', 'longtable', 0, 1, true, 'table', 'table', 'full', 'Routed to ParseTabular; longtable directives stripped'),
  ('wrapfigure',         'environment', 'wrapfig', 2, 1, true, 'figure', 'figure', 'full', 'Routed to figure handler'),
  ('tabular',            'environment', NULL, 1, 1, true, 'table', 'table', 'full', 'Bare \begin{tabular} produces ImportTable (SG-117)')
ON CONFLICT (name, kind, package_slug) DO UPDATE
  SET coverage_level = EXCLUDED.coverage_level,
      arity = EXCLUDED.arity,
      optional_arity = EXCLUDED.optional_arity,
      expects_body = EXCLUDED.expects_body,
      semantic_category = EXCLUDED.semantic_category,
      maps_to_block_type = EXCLUDED.maps_to_block_type,
      notes = EXCLUDED.notes;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Catalog seeds are authoritative; a Down() that delete-removes
            // these rows would re-introduce the leak fallback. Intentional
            // no-op (mirrors AddLatexCatalog seed migration policy).
        }
    }
}
