-- =====================================================================
--  Scenario System — Phase 2, batch 1: promote spec-covered flow
--  scenarios L1 -> L2.
--
--  Steps are authored from the executable walkthroughs in
--  lilia-web-editor/e2e/tests/flow.spec.ts (each test is fingerprinted
--  with scenario(<slug>)). This is the AI-assisted L1->L2 promotion the
--  plan calls for — see lilia-docs/scenario-system/plan.md (Phase 2).
--
--  Batch 1 of 2 — core editor interactions. Batch 2 covers the ribbon,
--  outline, preview, rails, comments and formula scenarios.
--
--  Idempotent: each row is guarded by NOT EXISTS on version_number = 2.
-- =====================================================================

BEGIN;

WITH authored (slug, title, descr, steps) AS (
  VALUES
  ('flow.open-document',
   'Open a document in Flow mode',
   'Open a document and land in the Flow editor.',
   '[
     {"step_kind":"setup","description":"Open a document from your dashboard.","user_visible_outcome":"The browser navigates to the Flow editor at /document/<id>."},
     {"step_kind":"assert","description":"The Flow editor shell mounts.","user_visible_outcome":"The continuous-prose canvas, the formatting ribbon and the side rails are all visible."},
     {"step_kind":"assert","description":"Confirm the Block / Flow toggle reports Flow as the active mode.","user_visible_outcome":"The toggle is set to Flow."}
   ]'::jsonb),

  ('flow.type-prose-persists',
   'Typed prose persists across a reload',
   'Type prose in the Flow editor and confirm it survives a reload.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a line of prose."},
     {"step_kind":"action","action_kind":"press","description":"Press Ctrl+S to save the document."},
     {"step_kind":"assert","description":"Reload the page.","user_visible_outcome":"The prose you typed is still there — it persisted to the server."}
   ]'::jsonb),

  ('flow.autosave-debounced',
   'Edits autosave after the debounce window',
   'Type in the Flow editor and let the debounced autosave persist it without a manual save.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a line of prose, then stop and wait."},
     {"step_kind":"assert","description":"Wait out the autosave debounce window — no manual save.","user_visible_outcome":"The edit reaches the server on its own; the save indicator settles on up to date."}
   ]'::jsonb),

  ('flow.slash-menu-opens',
   'The slash menu opens on /',
   'Open the slash command menu by typing a forward slash.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a forward slash."},
     {"step_kind":"assert","description":"The slash command menu opens.","user_visible_outcome":"A floating menu of block types appears, anchored at the caret."}
   ]'::jsonb),

  ('flow.slash-menu-filters',
   'The slash menu filters as you type',
   'Narrow the slash command menu by typing part of a block name.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"type","description":"Type a forward slash to open the slash command menu."},
     {"step_kind":"assert","description":"The full list of block types is shown.","user_visible_outcome":"Every insertable block type is listed."},
     {"step_kind":"action","action_kind":"type","description":"Type part of a block name, such as \"head\"."},
     {"step_kind":"assert","description":"The menu narrows to the matching commands.","user_visible_outcome":"Only block types matching what you typed remain listed."}
   ]'::jsonb),

  ('flow.markdown-bold',
   'Markdown input rule applies bold',
   'Bold text in the Flow editor with the Markdown ** input rule.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a word wrapped in double asterisks, like **bold**."},
     {"step_kind":"assert","description":"The Markdown input rule fires on the closing asterisks.","user_visible_outcome":"The wrapped word renders in bold and the asterisks disappear."}
   ]'::jsonb),

  ('flow.markdown-italic',
   'Markdown input rule applies italic',
   'Italicise text in the Flow editor with the Markdown * input rule.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a word wrapped in single asterisks, like *italic*."},
     {"step_kind":"assert","description":"The Markdown input rule fires on the closing asterisk.","user_visible_outcome":"The wrapped word renders in italic and the asterisks disappear."}
   ]'::jsonb),

  ('flow.inline-math-renders',
   'Inline math renders from $...$',
   'Write inline math in the Flow editor with the $...$ input rule.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor."},
     {"step_kind":"action","action_kind":"type","description":"Type a short expression wrapped in dollar signs, like $x^2$."},
     {"step_kind":"assert","description":"The $...$ input rule converts the text to an inline-math node.","user_visible_outcome":"The expression renders as typeset math inside the prose."}
   ]'::jsonb),

  ('flow.toolbox-opens',
   'The Toolbox insert pane opens',
   'Open the Flow editor and browse the pinned Toolbox insert pane.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The Flow editor loads with the Toolbox pinned along the left."},
     {"step_kind":"assert","description":"The Toolbox insert pane is visible.","user_visible_outcome":"The Toolbox shows its insert content, grouped into sections."},
     {"step_kind":"assert","description":"Browse the Toolbox sections — text, math, media and more.","user_visible_outcome":"Each content section is available to expand and insert from."}
   ]'::jsonb),

  ('flow.toolbox-search-filters',
   'Toolbox search filters the insert items',
   'Filter the Toolbox insert items with its search field.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor with the Toolbox visible.","user_visible_outcome":"The Toolbox insert pane is open."},
     {"step_kind":"action","action_kind":"type","description":"Type a block name into the Toolbox search field, such as \"heading\"."},
     {"step_kind":"assert","description":"The Toolbox filters to matching insert items.","user_visible_outcome":"Only insert items matching the search term remain shown."}
   ]'::jsonb),

  ('flow.command-palette-opens',
   'The command palette opens on Cmd+K',
   'Open the global command palette with the keyboard.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"press","description":"Press Ctrl+K (or Cmd+K on macOS)."},
     {"step_kind":"assert","description":"The command palette opens.","user_visible_outcome":"A search dialog appears, ready to jump to a section or run any command."}
   ]'::jsonb)
)
INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT s.id, 2, 'l2', a.title, a.descr, a.steps, 'llm_draft', 'claude-phase2'
FROM authored a
JOIN e2e.scenario s ON s.slug = a.slug
WHERE NOT EXISTS (
  SELECT 1 FROM e2e.scenario_version v
  WHERE v.scenario_id = s.id AND v.version_number = 2
);

UPDATE e2e.scenario s
SET detail_level = 'l2',
    description = v.description,
    current_version_id = v.id,
    updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = s.id
  AND v.version_number = 2
  AND s.slug IN (
    'flow.open-document', 'flow.type-prose-persists', 'flow.autosave-debounced',
    'flow.slash-menu-opens', 'flow.slash-menu-filters', 'flow.markdown-bold',
    'flow.markdown-italic', 'flow.inline-math-renders', 'flow.toolbox-opens',
    'flow.toolbox-search-filters', 'flow.command-palette-opens'
  );

DO $$
DECLARE n INT;
BEGIN
  SELECT count(*) INTO n
  FROM e2e.scenario s
  JOIN e2e.module m ON m.id = s.module_id
  WHERE m.slug = 'flow' AND s.detail_level = 'l2';
  RAISE NOTICE 'flow scenarios at L2 after batch 1: %', n;
END$$;

COMMIT;
