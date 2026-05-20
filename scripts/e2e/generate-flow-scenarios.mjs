#!/usr/bin/env node
/**
 * Generate the Flow-editor (continuous-prose mode) scenario seed.
 *
 * The Studio block editor is catalogued by 026 + the block-deep seed
 * 028. The Flow editor (`/document/:id`, EditorPage.tsx) had no
 * scenarios at all. This script produces:
 *
 *   1. A hand-curated set of ~27 KEY flow scenarios — the flows that
 *      are unique to prose mode (open document, type prose, markdown
 *      input rules, Block/Flow toggle, Toolbox, ribbon, preview, ⌘K).
 *
 *   2. A block-deep matrix — 13 block types × the flow-mode actions
 *      (markdown shortcut, slash, Toolbox, formula editor, …). Prose
 *      mode creates/edits blocks differently from the block-cards UI,
 *      so the action taxonomy differs from generate-block-scenarios.mjs.
 *
 * Outputs:
 *
 *   database/030_seed_flow_scenarios.sql
 *     Executable seed (NOT auto-applied). Idempotent.
 *
 *   ../lilia-docs/launch-readiness/2026-05-20-flow-scenarios-preview.md
 *     Human-readable review table.
 *
 * Usage:
 *   node scripts/e2e/generate-flow-scenarios.mjs
 *
 * Depends on 029_seed_flow_catalogue.sql having created the `flow`
 * module + surfaces (every surface_slug below must exist).
 */

import { createHash } from 'node:crypto';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..');

const MILESTONE = 'flow-seed-2026-05-20';
const GENERATED_BY = 'claude-flow-seed';

// =====================================================================
//  Block types (canonical) + families. Same 13 as the block seed.
//
//  markdownCreatable — has a TipTap-style input rule at line start
//  ("# " heading, "- " list, "> " quote, ``` code, "$$" equation,
//  "---" page break). Paragraph is the default node, not a shortcut.
// =====================================================================

const BLOCK_TYPES = [
  { slug: 'paragraph',       family: 'text-y',    name: 'Paragraph',         markdownCreatable: false },
  { slug: 'heading',         family: 'text-y',    name: 'Heading',           markdownCreatable: true  },
  { slug: 'list',            family: 'text-y',    name: 'List',              markdownCreatable: true  },
  { slug: 'blockquote',      family: 'text-y',    name: 'Block quote',       markdownCreatable: true  },
  { slug: 'theorem',         family: 'text-y',    name: 'Theorem',           markdownCreatable: false },
  { slug: 'abstract',        family: 'text-y',    name: 'Abstract',          markdownCreatable: false },
  { slug: 'figure',          family: 'media',     name: 'Figure',            markdownCreatable: false },
  { slug: 'equation',        family: 'math',      name: 'Equation',          markdownCreatable: true  },
  { slug: 'code',            family: 'code',      name: 'Code',              markdownCreatable: true  },
  { slug: 'table',           family: 'tabular',   name: 'Table',             markdownCreatable: false },
  { slug: 'bibliography',    family: 'auto',      name: 'Bibliography',      markdownCreatable: false },
  { slug: 'tableOfContents', family: 'auto',      name: 'Table of contents', markdownCreatable: false },
  { slug: 'pageBreak',       family: 'separator', name: 'Page break',        markdownCreatable: true  },
];

// =====================================================================
//  Flow-mode actions. `surface` is the e2e.surface slug that hosts it
//  (must exist after 029). Export actions live on the `export` module's
//  export-dialog, exactly like the block seed.
// =====================================================================

const ACTIONS = {
  'create-via-markdown':     { name: 'Create via markdown shortcut',  surface: 'flow-canvas',         applies: 'markdown',    priority: 'p0', group: 'Creation'  },
  'create-via-slash':        { name: 'Create via slash command',      surface: 'flow-slash-menu',     applies: 'all',         priority: 'p0', group: 'Creation'  },
  'create-via-toolbox':      { name: 'Create via Toolbox pane',       surface: 'flow-toolbox',        applies: 'all',         priority: 'p1', group: 'Creation'  },
  'edit-inline':             { name: 'Edit inline',                   surface: 'flow-canvas',         applies: 'inline',      priority: 'p0', group: 'Editing'   },
  'edit-via-formula-editor': { name: 'Edit via formula editor',       surface: 'flow-formula-panel',  applies: 'math',        priority: 'p0', group: 'Editing'   },
  'edit-via-drawer':         { name: 'Edit via format rail',          surface: 'flow-format-rail',    applies: 'drawer-edit', priority: 'p1', group: 'Editing'   },
  'convert-via-slash':       { name: 'Convert via slash command',     surface: 'flow-slash-menu',     applies: 'convertible', priority: 'p1', group: 'Lifecycle' },
  'delete-via-backspace':    { name: 'Delete via backspace',          surface: 'flow-canvas',         applies: 'inline',      priority: 'p2', group: 'Lifecycle' },
  'reorder-via-drag':        { name: 'Reorder via drag handle',       surface: 'flow-canvas',         applies: 'all',         priority: 'p1', group: 'Lifecycle' },
  'render-preview':          { name: 'Render in preview pane',        surface: 'flow-preview-panel',  applies: 'all',         priority: 'p0', group: 'Output'    },
  'export-to-latex':         { name: 'Export to LaTeX',               surface: 'export-dialog',       applies: 'all',         priority: 'p0', group: 'Output'    },
  'export-to-markdown':      { name: 'Export to Markdown',            surface: 'export-dialog',       applies: 'all',         priority: 'p1', group: 'Output'    },
};

const GROUP_ORDER = ['Creation', 'Editing', 'Lifecycle', 'Output'];

// =====================================================================
//  Applicability gate — true iff this (block, action) is meaningful in
//  prose mode.
// =====================================================================

function applies(block, action) {
  switch (action.applies) {
    case 'all':
      return true;
    case 'markdown':
      return block.markdownCreatable;
    case 'inline':
      // Inline-editable: text-y blocks + code.
      return block.family === 'text-y' || block.family === 'code';
    case 'drawer-edit':
      // Blocks with non-trivial config edited via the format rail.
      return ['figure', 'table', 'theorem', 'code'].includes(block.slug);
    case 'math':
      return block.family === 'math';
    case 'convertible':
      // Text-y siblings convert freely between each other.
      return block.family === 'text-y';
    default:
      return false;
  }
}

// =====================================================================
//  Key flow scenarios — hand-curated, flow-unique. Not block-deep.
// =====================================================================

const KEY_SCENARIOS = [
  { slug: 'flow.open-document',              surface: 'flow-page',             crit: 'p0', title: 'Open a document in Flow mode',                     desc: '/document/:id mounts the prose editor for a known doc id.' },
  { slug: 'flow.type-prose-persists',        surface: 'flow-canvas',           crit: 'p0', title: 'Typed prose persists across a reload',             desc: 'Text typed into the canvas survives a reload of /document/:id.' },
  { slug: 'flow.autosave-debounced',         surface: 'flow-canvas',           crit: 'p0', title: 'Edits autosave after the debounce window',         desc: 'useAutoSave flushes a batched update ~3s after the last keystroke.' },
  { slug: 'flow.toggle-to-block',            surface: 'block-flow-toggle',     crit: 'p0', title: 'Block/Flow toggle switches to Block mode',         desc: 'Clicking Block navigates /document/:id to /studio/:id for the same doc.' },
  { slug: 'flow.toggle-to-flow',             surface: 'block-flow-toggle',     crit: 'p0', title: 'Block/Flow toggle switches to Flow mode',          desc: 'Clicking Flow navigates /studio/:id to /document/:id for the same doc.' },
  { slug: 'flow.markdown-bold',              surface: 'flow-canvas',           crit: 'p1', title: 'Markdown input rule applies bold',                 desc: 'Typing **text** marks the run bold in the prose canvas.' },
  { slug: 'flow.markdown-italic',            surface: 'flow-canvas',           crit: 'p1', title: 'Markdown input rule applies italic',               desc: 'Typing *text* / _text_ marks the run italic in the prose canvas.' },
  { slug: 'flow.inline-math-renders',        surface: 'flow-canvas',           crit: 'p1', title: 'Inline math renders from $...$',                   desc: 'Typing $x^2$ inline produces a KaTeX-rendered inline equation.' },
  { slug: 'flow.slash-menu-opens',           surface: 'flow-slash-menu',       crit: 'p0', title: 'Slash menu opens on "/"',                          desc: 'Pressing "/" in the canvas opens the slash command menu at the cursor.' },
  { slug: 'flow.slash-menu-filters',         surface: 'flow-slash-menu',       crit: 'p1', title: 'Slash menu filters as you type',                   desc: 'Typing after "/" narrows the menu to matching block types.' },
  { slug: 'flow.toolbox-opens',              surface: 'flow-toolbox',          crit: 'p0', title: 'Toolbox insert pane opens with all sections',      desc: 'The pinned Toolbox shows the 6 sections: text, lists, math, media, refs, layout.' },
  { slug: 'flow.toolbox-search-filters',     surface: 'flow-toolbox',          crit: 'p1', title: 'Toolbox search filters insert items',              desc: 'Typing in the Toolbox search field narrows the visible block items.' },
  { slug: 'flow.toolbox-insert-at-cursor',   surface: 'flow-toolbox',          crit: 'p0', title: 'Toolbox item inserts a block at the cursor',       desc: 'Clicking a Toolbox item inserts that block at the current selection.' },
  { slug: 'flow.ribbon-tab-switch',          surface: 'flow-ribbon',           crit: 'p2', title: 'Ribbon tabs switch command groups',                desc: 'Switching Home/Cite/Review/View swaps the ribbon command set.' },
  { slug: 'flow.ribbon-format-bold',         surface: 'flow-ribbon',           crit: 'p1', title: 'Ribbon Bold formats the selection',                desc: 'Selecting text and clicking ribbon Bold marks it bold.' },
  { slug: 'flow.outline-lists-headings',     surface: 'flow-outline-panel',    crit: 'p1', title: 'Outline panel lists document headings',            desc: 'The outline reflects every heading in the prose document, in order.' },
  { slug: 'flow.outline-click-scrolls',      surface: 'flow-outline-panel',    crit: 'p1', title: 'Clicking an outline entry scrolls the canvas',     desc: 'Selecting an outline heading scrolls the canvas to that section.' },
  { slug: 'flow.preview-renders',            surface: 'flow-preview-panel',    crit: 'p0', title: 'Preview panel renders the document',               desc: 'The preview pane shows a non-empty rendering of the prose document.' },
  { slug: 'flow.preview-toggle',             surface: 'flow-preview-panel',    crit: 'p1', title: 'Preview panel can be shown and hidden',            desc: 'The View ribbon toggle shows/hides the preview pane.' },
  { slug: 'flow.format-rail-opens',          surface: 'flow-format-rail',      crit: 'p1', title: 'Format rail opens for the active block',           desc: 'The format rail shows formatting + config for the block at the cursor.' },
  { slug: 'flow.validate-rail-lists-issues', surface: 'flow-validate-rail',    crit: 'p1', title: 'Validate rail lists LaTeX validation issues',      desc: 'The validate rail enumerates validation issues for the document.' },
  { slug: 'flow.command-palette-opens',      surface: 'flow-command-palette',  crit: 'p0', title: 'Command palette opens on Cmd+K',                   desc: 'Pressing Cmd/Ctrl+K opens the command palette over the prose editor.' },
  { slug: 'flow.command-palette-runs',       surface: 'flow-command-palette',  crit: 'p1', title: 'Command palette runs a selected action',           desc: 'Choosing an action from the palette executes it and closes the palette.' },
  { slug: 'flow.activity-bar-switch-panel',  surface: 'flow-activity-bar',     crit: 'p1', title: 'Activity bar switches the docked side panel',      desc: 'Clicking an activity-bar icon swaps the right-rail panel.' },
  { slug: 'flow.comments-add-thread',        surface: 'flow-comments-panel',   crit: 'p1', title: 'A comment thread can be added in Flow mode',       desc: 'The comments panel composes a new thread on the prose document.' },
  { slug: 'flow.document-settings-opens',    surface: 'flow-document-settings',crit: 'p1', title: 'Document settings dialog opens from the topbar',   desc: 'Clicking the document stamp opens the class/font/paper settings dialog.' },
  { slug: 'flow.formula-panel-inserts',      surface: 'flow-formula-panel',    crit: 'p1', title: 'Formula panel inserts a formula into the canvas',  desc: 'Picking a formula from the panel inserts it at the cursor.' },
];

// =====================================================================
//  Build the row set.
// =====================================================================

function fingerprint(slug) {
  return createHash('sha256').update(slug).digest('hex').slice(0, 16);
}

function buildRows() {
  const rows = [];

  // Key scenarios.
  for (const k of KEY_SCENARIOS) {
    rows.push({
      kind: 'key',
      slug: k.slug,
      title: k.title,
      description: k.desc,
      module_slug: 'flow',
      surface_slug: k.surface,
      criticality: k.crit,
      fingerprint: fingerprint(k.slug),
    });
  }

  // Block-deep matrix.
  for (const block of BLOCK_TYPES) {
    for (const [actionSlug, action] of Object.entries(ACTIONS)) {
      if (!applies(block, action)) continue;
      const slug = `flow.${block.slug}.${actionSlug}`;
      rows.push({
        kind: 'block',
        slug,
        title: `${block.name}: ${action.name.toLowerCase()}`,
        description: null,
        module_slug: action.surface === 'export-dialog' ? 'export' : 'flow',
        surface_slug: action.surface,
        criticality: action.priority,
        fingerprint: fingerprint(slug),
        block_slug: block.slug,
        block_name: block.name,
        block_family: block.family,
        action_slug: actionSlug,
        action_name: action.name,
        action_group: action.group,
      });
    }
  }

  return rows;
}

// =====================================================================
//  SQL generator
// =====================================================================

function q(s) {
  return `'${String(s).replace(/'/g, "''")}'`;
}
function qOrNull(s) {
  return s === null || s === undefined ? 'NULL' : q(s);
}

function renderSql(rows) {
  const keyCount = rows.filter((r) => r.kind === 'key').length;
  const blockCount = rows.filter((r) => r.kind === 'block').length;

  let sql = '';
  sql += '-- =====================================================================\n';
  sql += '--  Flow-editor scenario seed — L1 stubs for the continuous-prose\n';
  sql += `--  editor (/document/:id). ${rows.length} scenarios: ${keyCount} key flow\n`;
  sql += `--  scenarios + ${blockCount} block-deep stubs (13 block types x flow\n`;
  sql += '--  actions). Generated by scripts/e2e/generate-flow-scenarios.mjs.\n';
  sql += '--\n';
  sql += '--  Requires 029_seed_flow_catalogue.sql (the `flow` module +\n';
  sql += '--  surfaces) to have been applied first.\n';
  sql += '--\n';
  sql += '--  Idempotent: ON CONFLICT(slug) DO NOTHING for the scenario rows,\n';
  sql += '--  guarded NOT EXISTS for the version rows. Re-run safe.\n';
  sql += '--\n';
  sql += "--  Seeded scenarios start in review_state='draft', detail_level\n";
  sql += "--  'l1'. Promote to 'l2'/'l3' as Playwright tests are written.\n";
  sql += '-- =====================================================================\n\n';
  sql += 'BEGIN;\n\n';
  sql += 'WITH m AS (SELECT id, slug FROM e2e.module),\n';
  sql += '     s AS (SELECT id, module_id, slug FROM e2e.surface)\n';
  sql += 'INSERT INTO e2e.scenario (\n';
  sql += '  slug, title, description, module_id, target_surface_id,\n';
  sql += '  criticality, detail_level, review_state, execution_mode,\n';
  sql += '  template, automation_content, milestone, created_by\n';
  sql += ')\n';
  sql += 'SELECT v.slug,\n';
  sql += '       v.title,\n';
  sql += '       v.description,\n';
  sql += '       m.id,\n';
  sql += '       CASE WHEN v.surface_slug IS NULL THEN NULL\n';
  sql += '            ELSE (SELECT id FROM s WHERE module_id = m.id AND slug = v.surface_slug)\n';
  sql += '       END,\n';
  sql += "       v.criticality, 'l1', 'draft', 'integration',\n";
  sql += `       'standard', v.fingerprint, ${q(MILESTONE)}, ${q(GENERATED_BY)}\n`;
  sql += 'FROM (VALUES\n';
  sql += rows
    .map((r, i) => {
      const tail = i < rows.length - 1 ? ',' : '';
      return `  (${q(r.module_slug)}, ${q(r.surface_slug)}, ${q(r.slug)}, ${q(r.title)}, ${qOrNull(r.description)}, ${q(r.fingerprint)}, ${q(r.criticality)})${tail}`;
    })
    .join('\n');
  sql += '\n';
  sql += ') AS v(module_slug, surface_slug, slug, title, description, fingerprint, criticality)\n';
  sql += 'JOIN m ON m.slug = v.module_slug\n';
  sql += 'ON CONFLICT (slug) DO NOTHING;\n\n';

  sql += '-- Insert version 1 (L1, generated) for every seeded scenario.\n';
  sql += 'INSERT INTO e2e.scenario_version (\n';
  sql += '  scenario_id, version_number, detail_level, title, description,\n';
  sql += '  steps, generation_provenance, generated_by\n';
  sql += ')\n';
  sql += 'SELECT\n';
  sql += "  sc.id, 1, 'l1', sc.title, sc.description,\n";
  sql += `  '[]'::jsonb, 'llm_draft', ${q(GENERATED_BY)}\n`;
  sql += 'FROM e2e.scenario sc\n';
  sql += `WHERE sc.milestone = ${q(MILESTONE)}\n`;
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
  sql += `  AND sc.milestone = ${q(MILESTONE)};\n\n`;

  sql += 'DO $$\n';
  sql += 'DECLARE c INTEGER;\n';
  sql += 'BEGIN\n';
  sql += `  SELECT COUNT(*) INTO c FROM e2e.scenario WHERE milestone = ${q(MILESTONE)};\n`;
  sql += "  RAISE NOTICE 'flow scenarios in DB: %', c;\n";
  sql += 'END$$;\n\n';
  sql += 'COMMIT;\n';
  return sql;
}

// =====================================================================
//  Markdown preview generator
// =====================================================================

function renderMarkdown(rows) {
  const keyRows = rows.filter((r) => r.kind === 'key');
  const blockRows = rows.filter((r) => r.kind === 'block');

  const byBlock = new Map();
  for (const r of blockRows) {
    if (!byBlock.has(r.block_slug)) byBlock.set(r.block_slug, []);
    byBlock.get(r.block_slug).push(r);
  }

  const totals = {
    p0: rows.filter((r) => r.criticality === 'p0').length,
    p1: rows.filter((r) => r.criticality === 'p1').length,
    p2: rows.filter((r) => r.criticality === 'p2').length,
  };

  let md = '';
  md += '# Flow-editor scenarios — review preview\n\n';
  md += `**Generated:** ${new Date().toISOString().slice(0, 10)}.\n\n`;
  md += `**Total:** ${rows.length} scenarios — ${keyRows.length} key flow scenarios `;
  md += `+ ${blockRows.length} block-deep stubs across ${byBlock.size} block types.\n`;
  md += `**Mix:** ${totals.p0} P0 · ${totals.p1} P1 · ${totals.p2} P2.\n\n`;
  md += 'The Flow editor (`/document/:id`, continuous-prose mode) had **no**\n';
  md += 'e2e module, surfaces or scenarios. The catalogue is added by\n';
  md += '`database/029_seed_flow_catalogue.sql`; this file previews the\n';
  md += 'matching scenario seed `database/030_seed_flow_scenarios.sql`.\n\n';
  md += 'Both SQL files are **staged but NOT applied**. Review here, then\n';
  md += 'apply (see "Next steps").\n\n';

  md += '## How to read this\n\n';
  md += '- **Key flow scenarios** cover prose-mode-unique flows (open\n';
  md += '  document, markdown input rules, Block/Flow toggle, Toolbox,\n';
  md += '  ribbon, preview, command palette).\n';
  md += '- **Block-deep** stubs are 13 block types × the flow-mode\n';
  md += '  actions, grouped per block into Creation / Editing / Lifecycle\n';
  md += '  / Output clusters (same clusters as the block-cards seed).\n';
  md += '- Each row is one L1 (intent-only) stub. Steps + selectors come\n';
  md += '  when scenarios are promoted to L2/L3.\n';
  md += '- **automation_content** is the stable fingerprint linking a\n';
  md += '  Playwright test to the row — use it verbatim in `scenario()`.\n';
  md += '- Strike a row (`~~Title~~`) to mark "do not seed".\n\n';

  md += '## Flow-mode action taxonomy\n\n';
  md += 'Prose mode creates/edits blocks differently from the block-cards\n';
  md += 'UI, so the action set differs from the block-cards seed:\n\n';
  md += '| Action | Surface | Applies to |\n';
  md += '|---|---|---|\n';
  md += '| `create-via-markdown` | `flow-canvas` | heading, list, blockquote, code, equation, pageBreak |\n';
  md += '| `create-via-slash` | `flow-slash-menu` | all block types |\n';
  md += '| `create-via-toolbox` | `flow-toolbox` | all block types |\n';
  md += '| `edit-inline` | `flow-canvas` | text-y + code |\n';
  md += '| `edit-via-formula-editor` | `flow-formula-panel` | equation |\n';
  md += '| `edit-via-drawer` | `flow-format-rail` | figure, table, theorem, code |\n';
  md += '| `convert-via-slash` | `flow-slash-menu` | text-y blocks |\n';
  md += '| `delete-via-backspace` | `flow-canvas` | text-y + code |\n';
  md += '| `reorder-via-drag` | `flow-canvas` | all block types |\n';
  md += '| `render-preview` | `flow-preview-panel` | all block types |\n';
  md += '| `export-to-latex` / `export-to-markdown` | `export-dialog` | all block types |\n\n';

  // Key scenarios table.
  md += `## Key flow scenarios (${keyRows.length})\n\n`;
  md += '| Crit | Title | Target surface | automation_content |\n';
  md += '|---|---|---|---|\n';
  for (const r of keyRows) {
    md += `| ${r.criticality.toUpperCase()} | ${r.title} | \`${r.surface_slug}\` | \`${r.fingerprint}\` |\n`;
  }
  md += '\n';

  // Per-block sections.
  md += '## Block-deep flow scenarios\n\n';
  for (const block of BLOCK_TYPES) {
    const rowsForBlock = byBlock.get(block.slug) ?? [];
    md += `### ${block.name} (\`${block.slug}\`)\n\n`;
    md += `*Family:* ${block.family}.  *Total:* ${rowsForBlock.length}.\n\n`;

    const byGroup = new Map();
    for (const r of rowsForBlock) {
      if (!byGroup.has(r.action_group)) byGroup.set(r.action_group, []);
      byGroup.get(r.action_group).push(r);
    }
    const shape = GROUP_ORDER.filter((g) => byGroup.has(g))
      .map((g) => `${g} ${byGroup.get(g).length}`)
      .join(' · ');
    md += `*Shape:* ${shape}\n\n`;

    for (const group of GROUP_ORDER) {
      const gRows = byGroup.get(group);
      if (!gRows || gRows.length === 0) continue;
      md += `#### ${group}\n\n`;
      md += '| Crit | Action | Title | Target surface | automation_content |\n';
      md += '|---|---|---|---|---|\n';
      for (const r of gRows) {
        md += `| ${r.criticality.toUpperCase()} | \`${r.action_slug}\` | ${r.title} | \`${r.surface_slug}\` | \`${r.fingerprint}\` |\n`;
      }
      md += '\n';
    }
  }

  md += '## Next steps after approval\n\n';
  md += '```bash\n';
  md += '# From the lilia-editor-api repo root, in order:\n';
  md += 'psql -h 127.0.0.1 -U lilia -d lilia -f database/029_seed_flow_catalogue.sql\n';
  md += 'psql -h 127.0.0.1 -U lilia -d lilia -f database/030_seed_flow_scenarios.sql\n';
  md += '```\n\n';
  md += 'Both seeds are additive (`ON CONFLICT DO NOTHING` / `DO UPDATE`),\n';
  md += 'so re-running is safe.\n';
  return md;
}

// =====================================================================
//  Run
// =====================================================================

const rows = buildRows();

// Guard: fingerprints must be unique (slug collisions would drop rows).
const seen = new Map();
for (const r of rows) {
  if (seen.has(r.fingerprint)) {
    throw new Error(
      `fingerprint collision: ${r.slug} vs ${seen.get(r.fingerprint)}`,
    );
  }
  seen.set(r.fingerprint, r.slug);
}

const sqlPath = resolve(REPO_ROOT, 'database', '030_seed_flow_scenarios.sql');
const docsPath = resolve(
  REPO_ROOT, '..', 'lilia-docs', 'launch-readiness',
  '2026-05-20-flow-scenarios-preview.md',
);

mkdirSync(dirname(sqlPath), { recursive: true });
mkdirSync(dirname(docsPath), { recursive: true });
writeFileSync(sqlPath, renderSql(rows));
writeFileSync(docsPath, renderMarkdown(rows));

const keyCount = rows.filter((r) => r.kind === 'key').length;
const blockCount = rows.filter((r) => r.kind === 'block').length;
console.log(`Generated ${rows.length} flow scenarios (${keyCount} key + ${blockCount} block-deep).`);
console.log(`  SQL:     ${sqlPath}`);
console.log(`  Preview: ${docsPath}`);
