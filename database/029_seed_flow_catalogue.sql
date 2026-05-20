-- =====================================================================
--  E2E catalogue seed — Flow editor (`/document/:id`, continuous prose).
--
--  The 026 catalogue covers the Studio *block* editor (`/studio/:id`).
--  The Flow editor — TipTap continuous-prose mode, EditorPage.tsx — had
--  NO module, NO surfaces, NO scenarios. This seed closes that gap:
--  one `flow` module + 18 flow surfaces + flow block-actions + entry
--  points. Scenarios land separately in 030_seed_flow_scenarios.sql.
--
--  Idempotent: ON CONFLICT on the natural keys. Additive — safe to
--  re-run. Seed data only; no schema change (per the schema policy).
--
--  Source: lilia-docs/launch-readiness/2026-05-18-e2e-scenario-db.md
--  + the Flow editor surface inventory in EditorPage.tsx.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
--  Module — `flow`, parallel to `studio` (separate route + component
--  tree). `studio` stays "the block editor"; `flow` is the prose mode.
-- ---------------------------------------------------------------------

INSERT INTO e2e.module (slug, name, description, criticality) VALUES
  ('flow', 'Flow editor (prose)',
   'Continuous-prose document editor at /document/:id — TipTap canvas, ribbon, Toolbox insert pane, side rails, preview.',
   'p0')
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      criticality = EXCLUDED.criticality,
      updated_at = NOW();

-- ---------------------------------------------------------------------
--  Flow-specific block actions — the prose editor creates / edits
--  blocks differently from the block-cards UI (markdown input rules,
--  the Toolbox pane, backspace-to-delete). Catalogue table only;
--  informational, mirrors the 026 block_action set.
-- ---------------------------------------------------------------------

INSERT INTO e2e.block_action (slug, name, description, expected_surface_kind) VALUES
  ('create-via-markdown',     'Create via markdown shortcut',  'Type a markdown input rule at line start ("# ", "- ", "> ", ``` ```, "$$", "---").', 'inline'),
  ('create-via-toolbox',      'Create via Toolbox insert pane','Click a block item in the pinned Toolbox insert surface.',                         'drawer'),
  ('edit-via-formula-editor', 'Edit via formula editor',       'Open the formula panel/modal to edit an equation block.',                          'modal'),
  ('delete-via-backspace',    'Delete via backspace',          'Backspace at the start of an empty block merges/removes it in prose flow.',         'inline')
ON CONFLICT (slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      expected_surface_kind = EXCLUDED.expected_surface_kind;

-- ---------------------------------------------------------------------
--  Surfaces — the Flow editor's 18 surfaces (EditorPage.tsx mounts).
-- ---------------------------------------------------------------------

WITH m AS (SELECT id, slug FROM e2e.module)
INSERT INTO e2e.surface (module_id, slug, name, description, surface_kind, route_pattern, source_file, criticality)
SELECT m.id, v.slug, v.name, v.description, v.surface_kind, v.route_pattern, v.source_file, v.criticality
FROM (VALUES
  ('flow', 'flow-page',              'Flow editor page',          'Top-level /document/:id route, hosts the prose editor.',          'page',    '/document/:id', 'src/pages/EditorPage.tsx', 'p0'),
  ('flow', 'flow-canvas',            'Flow prose canvas',         'The TipTap continuous-prose editing surface.',                    'inline',  NULL, 'src/components/editor/BlockEditor.tsx', 'p0'),
  ('flow', 'flow-topbar',            'Flow top bar',              'Title input, Block/Flow toggle, share, settings, save badge.',    'inline',  NULL, 'src/components/editor/TopBar.tsx', 'p0'),
  ('flow', 'block-flow-toggle',      'Block / Flow mode toggle',  'Segment that switches between /studio (Block) and /document (Flow).', 'inline', NULL, 'src/components/editor/BlockFlowToggle.tsx', 'p0'),
  ('flow', 'flow-ribbon',            'Flow ribbon',               'Home / Cite / Review / View formatting ribbon (prose mode).',     'inline',  NULL, 'src/components/editor/EditorRibbon.tsx', 'p1'),
  ('flow', 'flow-toolbox',           'Toolbox insert pane',       'Pinned unified insert surface — block items in 6 sections.',      'drawer',  NULL, 'src/components/toolbox/Toolbox.tsx', 'p0'),
  ('flow', 'flow-slash-menu',        'Flow slash command menu',   'Popover triggered by "/" in the prose canvas.',                   'popover', NULL, 'src/components/editor/SlashCommandMenu.tsx', 'p0'),
  ('flow', 'flow-right-rail',        'Flow right rail',           'Tabbed right rail: Outline / Comments / History / Validate / Format.', 'inline', NULL, 'src/components/editor/RightRail.tsx', 'p1'),
  ('flow', 'flow-outline-panel',     'Outline panel',             'Document outline; click a heading to scroll the canvas.',         'drawer',  NULL, 'src/components/editor/OutlinePanel.tsx', 'p1'),
  ('flow', 'flow-format-rail',       'Format rail panel',         'Block / selection formatting + per-block config in prose mode.',  'drawer',  NULL, 'src/components/editor/FormatRailPanel.tsx', 'p1'),
  ('flow', 'flow-validate-rail',     'Validate rail panel',       'LaTeX validation issues listed for the prose document.',          'drawer',  NULL, 'src/components/editor/ValidateRailPanel.tsx', 'p1'),
  ('flow', 'flow-preview-panel',     'Flow preview panel',        'Live LaTeX / PDF preview pane beside the prose canvas.',          'inline',  NULL, 'src/components/editor/PreviewPanel.tsx', 'p0'),
  ('flow', 'flow-activity-bar',      'Flow activity bar',         'Left rail switching the docked side panel.',                      'inline',  NULL, 'src/components/editor/ActivityBar.tsx', 'p1'),
  ('flow', 'flow-command-palette',   'Flow command palette (⌘K)', 'Global keyboard-driven action palette in prose mode.',            'modal',   NULL, 'src/components/editor/CommandPalette.tsx', 'p0'),
  ('flow', 'flow-formula-panel',     'Formula panel',             'Side panel to insert / pick formulas into the prose canvas.',     'drawer',  NULL, 'src/components/editor/FormulaPanel.tsx', 'p1'),
  ('flow', 'flow-bibliography-panel','Bibliography panel (flow)', 'Citation library + DOI lookup docked in the prose editor.',       'drawer',  NULL, 'src/components/editor/BibliographyPanel.tsx', 'p1'),
  ('flow', 'flow-comments-panel',    'Comments panel',            'Threaded comments on the prose document.',                        'drawer',  NULL, 'src/components/editor/CommentsPanel.tsx', 'p1'),
  ('flow', 'flow-document-settings', 'Document settings (flow)',  'Class / font / paper / columns dialog from the flow topbar.',     'modal',   NULL, 'src/components/editor/DocumentSettingsDialog.tsx', 'p1')
) AS v(module_slug, slug, name, description, surface_kind, route_pattern, source_file, criticality)
JOIN m ON m.slug = v.module_slug
ON CONFLICT (module_id, slug) DO UPDATE
  SET name = EXCLUDED.name,
      description = EXCLUDED.description,
      surface_kind = EXCLUDED.surface_kind,
      route_pattern = EXCLUDED.route_pattern,
      source_file = EXCLUDED.source_file,
      criticality = EXCLUDED.criticality,
      updated_at = NOW();

-- ---------------------------------------------------------------------
--  Entry points — how each Flow surface is opened.
-- ---------------------------------------------------------------------

WITH s AS (
  SELECT su.id, m.slug AS module_slug, su.slug AS surface_slug
  FROM e2e.surface su JOIN e2e.module m ON m.id = su.module_id
)
INSERT INTO e2e.entry_point (target_surface_id, slug, description, opener_kind, shortcut_keys, criticality)
SELECT s.id, v.ep_slug, v.description, v.opener_kind, v.shortcut, v.criticality
FROM (VALUES
  -- Flow page openers
  ('flow', 'flow-page', 'from-block-toggle',   'Block/Flow toggle → Flow (from /studio)',     'toolbar_button',    NULL,  'p0'),
  ('flow', 'flow-page', 'dashboard-open-flow', 'Open a doc from the dashboard in Flow mode',  'deep_link',         NULL,  'p1'),

  -- Block/Flow toggle openers
  ('flow', 'block-flow-toggle', 'flow-topbar-segment', 'Mode segment centred in the topbar', 'toolbar_button',    NULL,  'p0'),

  -- Slash menu
  ('flow', 'flow-slash-menu', 'slash-in-canvas',  '"/" key in the prose canvas',             'keyboard_shortcut', '/',   'p0'),

  -- Toolbox insert pane
  ('flow', 'flow-toolbox', 'ribbon-insert',       'Ribbon Insert action / pinned Toolbox',   'toolbar_button',    NULL,  'p0'),
  ('flow', 'flow-toolbox', 'toolbox-search',      'Toolbox search field focus',              'toolbar_button',    NULL,  'p1'),

  -- Command palette
  ('flow', 'flow-command-palette', 'cmd-k-flow',  '⌘K / Ctrl+K in the prose editor',         'keyboard_shortcut', '⌘K',  'p0'),

  -- Preview panel
  ('flow', 'flow-preview-panel', 'ribbon-view-preview', 'Ribbon View tab → Preview toggle',  'toolbar_button',    NULL,  'p0'),

  -- Right-rail panels
  ('flow', 'flow-outline-panel',  'activity-outline', 'Activity bar → Outline',              'toolbar_button',    NULL,  'p1'),
  ('flow', 'flow-validate-rail',  'activity-validate','Activity bar → Validate',             'toolbar_button',    NULL,  'p1'),
  ('flow', 'flow-comments-panel', 'activity-comments','Activity bar → Comments',             'toolbar_button',    NULL,  'p1'),

  -- Document settings
  ('flow', 'flow-document-settings', 'flow-stamp-click', 'Click the document stamp in the flow topbar', 'toolbar_button', NULL, 'p1')
) AS v(module_slug, surface_slug, ep_slug, description, opener_kind, shortcut, criticality)
JOIN s ON s.module_slug = v.module_slug AND s.surface_slug = v.surface_slug
ON CONFLICT (target_surface_id, slug) DO UPDATE
  SET description = EXCLUDED.description,
      opener_kind = EXCLUDED.opener_kind,
      shortcut_keys = EXCLUDED.shortcut_keys,
      criticality = EXCLUDED.criticality;

-- Sanity count for the migrator.
DO $$
DECLARE s INTEGER; ep INTEGER;
BEGIN
  SELECT COUNT(*) INTO s  FROM e2e.surface su
    JOIN e2e.module m ON m.id = su.module_id WHERE m.slug = 'flow';
  SELECT COUNT(*) INTO ep FROM e2e.entry_point ep2
    JOIN e2e.surface su ON su.id = ep2.target_surface_id
    JOIN e2e.module m ON m.id = su.module_id WHERE m.slug = 'flow';
  RAISE NOTICE 'flow catalogue seeded: % surfaces, % entry_points', s, ep;
END
$$;

COMMIT;
