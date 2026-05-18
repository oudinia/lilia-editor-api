#!/usr/bin/env node
/**
 * Generate the block-deep scenario stubs.
 *
 *   13 block types × applicable actions × applicable surfaces.
 *
 * Not every (block × action) pair is meaningful — `pageBreak` can't be
 * edited inline, `bibliography` is auto-generated, `tableOfContents`
 * doesn't have an "edit" surface in the usual sense. The matrix below
 * captures which combinations make sense.
 *
 * Outputs two artefacts:
 *
 *   1. lilia-docs/launch-readiness/2026-05-18-block-scenarios-preview.md
 *      Human-readable table for review. The user reads this BEFORE the
 *      SQL is applied.
 *
 *   2. database/028_seed_block_scenarios.sql
 *      Executable seed (NOT auto-applied). After the .md is approved,
 *      run psql -f database/028_seed_block_scenarios.sql.
 *
 *   Usage:
 *     node scripts/e2e/generate-block-scenarios.mjs
 */

import { createHash } from 'node:crypto';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..');

// =====================================================================
//  Block types (canonical) and their classification axes.
//
//  family — drives applicability rules:
//    'text-y'    — text + heading + list + blockquote + theorem + abstract
//                  Most actions apply. edit-inline, convert-via-slash,
//                  delete-via-keyboard all make sense.
//    'media'     — figure. Open-via-drawer for caption; drag-drop create.
//    'math'      — equation. Edit-via-modal (formula editor), not inline.
//    'code'      — code. Edit-inline plus drag-drop file upload.
//    'tabular'   — table. Edit per cell; edit-via-drawer for structure.
//    'auto'      — bibliography, tableOfContents. Auto-generated; can be
//                  inserted + deleted + rendered, but not edited.
//    'separator' — pageBreak. Insert + delete + render only.
// =====================================================================

const BLOCK_TYPES = [
  { slug: 'paragraph',       family: 'text-y',     name: 'Paragraph' },
  { slug: 'heading',         family: 'text-y',     name: 'Heading' },
  { slug: 'list',            family: 'text-y',     name: 'List' },
  { slug: 'blockquote',      family: 'text-y',     name: 'Block quote' },
  { slug: 'theorem',         family: 'text-y',     name: 'Theorem' },
  { slug: 'abstract',        family: 'text-y',     name: 'Abstract' },
  { slug: 'figure',          family: 'media',      name: 'Figure' },
  { slug: 'equation',        family: 'math',       name: 'Equation' },
  { slug: 'code',            family: 'code',       name: 'Code' },
  { slug: 'table',           family: 'tabular',    name: 'Table' },
  { slug: 'bibliography',    family: 'auto',       name: 'Bibliography' },
  { slug: 'tableOfContents', family: 'auto',       name: 'Table of contents' },
  { slug: 'pageBreak',       family: 'separator',  name: 'Page break' },
];

// =====================================================================
//  Actions. surface is the e2e.surface slug that hosts the action.
// =====================================================================

// Each action carries a `group` so the preview can render scenarios
// in semantic clusters per block type rather than a flat list.
//
//   Creation   — how a block enters the document
//   Editing    — how a block's content/config is changed
//   Lifecycle  — what happens after creation: convert / delete / move
//   Output     — how a block is rendered + exported
const ACTIONS = {
  'create-via-plus':      { name: 'Create via + button',           surface: 'block-type-menu',     applies: 'all',     priority: 'p0', group: 'Creation' },
  'create-via-slash':     { name: 'Create via slash command',      surface: 'slash-command-menu',  applies: 'all',     priority: 'p0', group: 'Creation' },
  'create-via-drag-drop': { name: 'Create via drag and drop',      surface: 'block-canvas',        applies: 'drag-drop', priority: 'p1', group: 'Creation' },
  'edit-inline':          { name: 'Edit inline',                   surface: 'block-card',          applies: 'inline',  priority: 'p0', group: 'Editing' },
  'edit-via-drawer':      { name: 'Edit via settings drawer',      surface: 'block-settings-drawer', applies: 'drawer-edit', priority: 'p1', group: 'Editing' },
  'edit-via-modal':       { name: 'Edit via formula editor',       surface: 'formula-editor',      applies: 'math',    priority: 'p0', group: 'Editing' },
  'convert-via-slash':    { name: 'Convert via slash command',     surface: 'slash-command-menu',  applies: 'convertible', priority: 'p1', group: 'Lifecycle' },
  'convert-via-kebab':    { name: 'Convert via kebab menu',        surface: 'block-card',          applies: 'convertible', priority: 'p2', group: 'Lifecycle' },
  'delete-via-kebab':     { name: 'Delete via kebab menu',         surface: 'block-card',          applies: 'all',     priority: 'p1', group: 'Lifecycle' },
  'delete-via-keyboard':  { name: 'Delete via keyboard shortcut',  surface: 'block-card',          applies: 'inline',  priority: 'p2', group: 'Lifecycle' },
  'reorder-via-drag':     { name: 'Reorder via drag handle',       surface: 'block-card',          applies: 'all',     priority: 'p1', group: 'Lifecycle' },
  'render-preview':       { name: 'Render in preview pane',        surface: 'block-canvas',        applies: 'all',     priority: 'p0', group: 'Output' },
  'export-to-latex':      { name: 'Export to LaTeX',               surface: 'export-dialog',       applies: 'all',     priority: 'p0', group: 'Output' },
  'export-to-markdown':   { name: 'Export to Markdown',            surface: 'export-dialog',       applies: 'all',     priority: 'p1', group: 'Output' },
};

// Order in which feature groups are rendered per block type.
const GROUP_ORDER = ['Creation', 'Editing', 'Lifecycle', 'Output'];

// =====================================================================
//  Applicability gate. Returns true iff this (block_type, action) is
//  a meaningful scenario.
// =====================================================================

function applies(block, actionSlug, action) {
  const f = block.family;
  switch (action.applies) {
    case 'all':
      return true;
    case 'inline':
      // Inline-editable text-y blocks + code (Monaco-style editor in-card).
      return f === 'text-y' || f === 'code';
    case 'drawer-edit':
      // Blocks with non-trivial config: figure (alt + width + label),
      // table (cols + rows), theorem (kind: lemma / definition), code
      // (language). Equation uses the modal path, not drawer.
      return ['media', 'tabular', 'text-y' /* theorem */, 'code'].includes(f) &&
             (block.slug === 'figure' || block.slug === 'table' ||
              block.slug === 'theorem' || block.slug === 'code');
    case 'math':
      return f === 'math';
    case 'drag-drop':
      // Today: figure (drop image). Future: code (drop source file),
      // table (paste CSV). Conservative for v1.
      return block.slug === 'figure';
    case 'convertible':
      // Convertible: text-y siblings can be swapped between each other.
      // Auto, separator, media, math, tabular blocks are NOT freely
      // convertible.
      return f === 'text-y';
    default:
      return false;
  }
}

// =====================================================================
//  Build the matrix.
// =====================================================================

function buildScenarios() {
  const rows = [];
  for (const block of BLOCK_TYPES) {
    for (const [actionSlug, action] of Object.entries(ACTIONS)) {
      if (!applies(block, actionSlug, action)) continue;

      const slug = `studio.${block.slug}.${actionSlug}`;
      const title = `${block.name}: ${action.name.toLowerCase()}`;
      const fingerprint = createHash('sha256').update(slug).digest('hex').slice(0, 16);
      const criticality = action.priority;
      const moduleSlug = 'studio';
      // Export actions live under the 'export' module surface.
      const targetSurfaceSlug = action.surface;
      const targetModuleSlug = action.surface === 'export-dialog' ? 'export' : 'studio';

      rows.push({
        slug,
        title,
        block_slug: block.slug,
        block_name: block.name,
        block_family: block.family,
        action_slug: actionSlug,
        action_name: action.name,
        action_group: action.group,
        target_module_slug: targetModuleSlug,
        target_surface_slug: targetSurfaceSlug,
        criticality,
        fingerprint,
      });
    }
  }
  return rows;
}

// =====================================================================
//  Markdown preview generator
// =====================================================================

function renderMarkdown(rows) {
  // Group by block, then by action category.
  const byBlock = new Map();
  for (const r of rows) {
    if (!byBlock.has(r.block_slug)) byBlock.set(r.block_slug, []);
    byBlock.get(r.block_slug).push(r);
  }

  const totals = {
    p0: rows.filter((r) => r.criticality === 'p0').length,
    p1: rows.filter((r) => r.criticality === 'p1').length,
    p2: rows.filter((r) => r.criticality === 'p2').length,
  };

  let md = '';
  md += '# Block-deep scenarios — review preview\n\n';
  md += `**Generated:** ${new Date().toISOString().slice(0, 10)}.\n\n`;
  md += `**Total:** ${rows.length} scenarios across ${byBlock.size} block types.\n`;
  md += `**Mix:** ${totals.p0} P0 · ${totals.p1} P1 · ${totals.p2} P2.\n\n`;
  md += 'This file is **review-only**. The matching SQL seed in\n';
  md += '`database/028_seed_block_scenarios.sql` is staged but NOT\n';
  md += 'applied. Approve here (edit / strike rows) → then run the SQL.\n\n';
  md += '## How to read this\n\n';
  md += '- One section per block type (paragraph, heading, …).\n';
  md += '- Within each block, scenarios are grouped into four feature\n';
  md += '  clusters so the shape per block is comparable at a glance:\n';
  md += '    - **Creation** — how a block enters the document.\n';
  md += '    - **Editing** — how a block\'s content / config changes.\n';
  md += '    - **Lifecycle** — convert / delete / reorder.\n';
  md += '    - **Output** — preview + export to LaTeX / Markdown.\n';
  md += '- Each row is one scenario stub at **L1 (intent only)**. Steps\n';
  md += '  + selectors come later when scenarios get promoted to L2/L3.\n';
  md += '- **automation_content** is the stable fingerprint that links\n';
  md += '  a Playwright test to this row. Use it verbatim in\n';
  md += '  `scenario("studio.paragraph.create-via-plus")`.\n';
  md += '- Strike a row (`~~Title~~`) to mark "do not seed".\n';
  md += '- Edit `Criticality` inline if you disagree with the default.\n\n';
  md += '## Applicability rules used\n\n';
  md += '| Block family | Inline edit | Drawer edit | Modal edit | Slash convert | Drag-drop |\n';
  md += '|---|---|---|---|---|---|\n';
  md += '| text-y | ✓ | (theorem only) | — | ✓ | — |\n';
  md += '| media (figure) | — | ✓ | — | — | ✓ |\n';
  md += '| math (equation) | — | — | ✓ | — | — |\n';
  md += '| code | ✓ | ✓ | — | — | — |\n';
  md += '| tabular (table) | — | ✓ | — | — | — |\n';
  md += '| auto (bib, TOC) | — | — | — | — | — |\n';
  md += '| separator (pageBreak) | — | — | — | — | — |\n\n';

  // Per-block tables, grouped by feature cluster (Creation /
  // Editing / Lifecycle / Output) within each block.
  for (const block of BLOCK_TYPES) {
    const blockRows = byBlock.get(block.slug) ?? [];
    md += `## ${block.name} (\`${block.slug}\`)\n\n`;
    md += `*Family:* ${block.family}.  *Total scenarios:* ${blockRows.length}.\n\n`;

    // Per-block counts by group for an at-a-glance shape.
    const byGroup = new Map();
    for (const r of blockRows) {
      if (!byGroup.has(r.action_group)) byGroup.set(r.action_group, []);
      byGroup.get(r.action_group).push(r);
    }
    const shape = GROUP_ORDER
      .filter((g) => byGroup.has(g))
      .map((g) => `${g} ${byGroup.get(g).length}`)
      .join(' · ');
    md += `*Shape:* ${shape}\n\n`;

    for (const group of GROUP_ORDER) {
      const gRows = byGroup.get(group);
      if (!gRows || gRows.length === 0) continue;
      md += `### ${group}\n\n`;
      md += '| Crit | Action | Title | Target surface | automation_content |\n';
      md += '|---|---|---|---|---|\n';
      for (const r of gRows) {
        md += `| ${r.criticality.toUpperCase()} | \`${r.action_slug}\` | ${r.title} | \`${r.target_surface_slug}\` | \`${r.fingerprint}\` |\n`;
      }
      md += '\n';
    }
  }

  md += '## Next steps after approval\n\n';
  md += '```bash\n';
  md += '# From repo root\n';
  md += 'psql -h 127.0.0.1 -U lilia -d lilia -f database/028_seed_block_scenarios.sql\n';
  md += '```\n\n';
  md += 'The seed is **additive** (`ON CONFLICT(slug) DO NOTHING`) so\n';
  md += 're-running won\'t duplicate anything.\n';
  return md;
}

// =====================================================================
//  SQL generator
// =====================================================================

function renderSql(rows) {
  let sql = '';
  sql += '-- =====================================================================\n';
  sql += '--  Block-deep scenario seed — L1 stubs generated from the applicability\n';
  sql += `--  matrix in scripts/e2e/generate-block-scenarios.mjs (${rows.length} rows).\n`;
  sql += '--\n';
  sql += '--  Idempotent: ON CONFLICT(slug) DO NOTHING for the scenario rows,\n';
  sql += '--  guarded NOT EXISTS for the version rows. Re-run safe.\n';
  sql += '--\n';
  sql += '--  Seeded scenarios start in review_state=\'draft\'. Promote to\n';
  sql += '--  \'approved\' once they have an L2/L3 implementation, or to\n';
  sql += '--  \'deprecated\' if you decide to drop one.\n';
  sql += '-- =====================================================================\n\n';
  sql += 'BEGIN;\n\n';
  sql += "WITH m AS (SELECT id, slug FROM e2e.module),\n";
  sql += "     s AS (SELECT id, module_id, slug FROM e2e.surface)\n";
  sql += 'INSERT INTO e2e.scenario (\n';
  sql += '  slug, title, description, module_id, target_surface_id,\n';
  sql += '  criticality, detail_level, review_state, execution_mode,\n';
  sql += "  template, automation_content, milestone, created_by\n";
  sql += ')\n';
  sql += 'SELECT v.slug,\n';
  sql += '       v.title,\n';
  sql += '       NULL,\n';
  sql += '       m.id,\n';
  sql += '       CASE WHEN v.surface_slug IS NULL THEN NULL\n';
  sql += '            ELSE (SELECT id FROM s WHERE module_id = m.id AND slug = v.surface_slug)\n';
  sql += '       END,\n';
  sql += "       v.criticality, 'l1', 'draft', 'integration',\n";
  sql += "       'standard', v.fingerprint, 'block-deep-seed-2026-05-18', 'claude-block-seed'\n";
  sql += 'FROM (VALUES\n';
  const valueRows = rows
    .map(
      (r, i) =>
        `  (${q(r.target_module_slug)}, ${q(r.target_surface_slug)}, ${q(r.slug)}, ${q(r.title)}, ${q(r.fingerprint)}, ${q(r.criticality)})${i < rows.length - 1 ? ',' : ''}`,
    )
    .join('\n');
  sql += valueRows;
  sql += '\n';
  sql += ') AS v(module_slug, surface_slug, slug, title, fingerprint, criticality)\n';
  sql += 'JOIN m ON m.slug = v.module_slug\n';
  sql += 'ON CONFLICT (slug) DO NOTHING;\n\n';
  sql += '-- Insert version 1 (L1, generated) for every seeded scenario.\n';
  sql += 'INSERT INTO e2e.scenario_version (\n';
  sql += '  scenario_id, version_number, detail_level, title, description,\n';
  sql += '  steps, generation_provenance, generated_by\n';
  sql += ')\n';
  sql += 'SELECT\n';
  sql += "  sc.id, 1, 'l1', sc.title, sc.description,\n";
  sql += "  '[]'::jsonb, 'llm_draft', 'claude-block-seed'\n";
  sql += 'FROM e2e.scenario sc\n';
  sql += "WHERE sc.milestone = 'block-deep-seed-2026-05-18'\n";
  sql += '  AND NOT EXISTS (\n';
  sql += '    SELECT 1 FROM e2e.scenario_version v\n';
  sql += '    WHERE v.scenario_id = sc.id AND v.version_number = 1\n';
  sql += '  );\n\n';
  sql += '-- Wire current_version_id pointer.\n';
  sql += 'UPDATE e2e.scenario sc\n';
  sql += 'SET current_version_id = v.id, updated_at = NOW()\n';
  sql += 'FROM e2e.scenario_version v\n';
  sql += 'WHERE v.scenario_id = sc.id\n';
  sql += '  AND v.version_number = 1\n';
  sql += '  AND sc.current_version_id IS DISTINCT FROM v.id\n';
  sql += "  AND sc.milestone = 'block-deep-seed-2026-05-18';\n\n";
  sql += "DO $$\n";
  sql += "DECLARE c INTEGER;\n";
  sql += "BEGIN\n";
  sql += "  SELECT COUNT(*) INTO c FROM e2e.scenario\n";
  sql += "    WHERE milestone = 'block-deep-seed-2026-05-18';\n";
  sql += "  RAISE NOTICE 'block-deep scenarios in DB: %', c;\n";
  sql += "END$$;\n\n";
  sql += 'COMMIT;\n';
  return sql;
}

function q(s) {
  return `'${String(s).replace(/'/g, "''")}'`;
}

// =====================================================================
//  Run
// =====================================================================

const rows = buildScenarios();
const docsPath = resolve(REPO_ROOT, '..', 'lilia-docs', 'launch-readiness',
  '2026-05-18-block-scenarios-preview.md');
const sqlPath = resolve(REPO_ROOT, 'database', '028_seed_block_scenarios.sql');

mkdirSync(dirname(docsPath), { recursive: true });
mkdirSync(dirname(sqlPath), { recursive: true });
writeFileSync(docsPath, renderMarkdown(rows));
writeFileSync(sqlPath, renderSql(rows));

console.log(`Generated ${rows.length} scenarios.`);
console.log(`  Preview: ${docsPath}`);
console.log(`  SQL:     ${sqlPath}`);
