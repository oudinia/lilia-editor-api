-- =====================================================================
--  Scenario System — Phase 2, batch 2: promote the remaining
--  spec-covered flow scenarios L1 -> L2.
--
--  Steps authored from lilia-web-editor/e2e/tests/flow.spec.ts, same as
--  batch 1 (032). This batch covers the toggle, ribbon, outline,
--  preview, format rail, command palette, activity bar, comments and
--  formula scenarios.
--
--  Deliberately excluded — the two scenarios flow.spec.ts marks
--  test.fixme() (UI not reachable yet): flow.validate-rail-lists-issues
--  and flow.document-settings-opens. They stay L1 until their UI is
--  wired — promoting them would mean shipping a walkthrough for a path
--  that does not work.
--
--  Idempotent: each row guarded by NOT EXISTS on version_number = 2.
-- =====================================================================

BEGIN;

WITH authored (slug, title, descr, steps) AS (
  VALUES
  ('flow.toggle-to-block',
   'Switch from Flow to Block mode',
   'Use the Block / Flow toggle to leave the Flow editor for the block-cards Studio.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas and the ribbon are up; the toggle reads Flow."},
     {"step_kind":"action","action_kind":"click","description":"Click the Block side of the Block / Flow toggle."},
     {"step_kind":"assert","description":"The editor switches to Block mode.","user_visible_outcome":"The URL changes to /studio/<id> and the block-cards Studio loads."}
   ]'::jsonb),

  ('flow.toggle-to-flow',
   'Switch from Block to Flow mode',
   'Use the Block / Flow toggle to enter the Flow editor from the block-cards Studio.',
   '[
     {"step_kind":"setup","description":"Open a document in Block mode — the Studio at /studio/<id>.","user_visible_outcome":"The block-cards Studio loads; the toggle reads Block."},
     {"step_kind":"action","action_kind":"click","description":"Click the Flow side of the Block / Flow toggle."},
     {"step_kind":"assert","description":"The editor switches to Flow mode.","user_visible_outcome":"The URL changes to /document/<id>, the continuous-prose canvas loads, and the toggle reads Flow."}
   ]'::jsonb),

  ('flow.toolbox-insert-at-cursor',
   'Insert a block from the Toolbox at the cursor',
   'Insert a block into the prose canvas by clicking a Toolbox item.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor with the Toolbox visible.","user_visible_outcome":"The Toolbox insert pane is open."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor where the block should go."},
     {"step_kind":"action","action_kind":"click","description":"Click the Heading 1 item in the Toolbox."},
     {"step_kind":"assert","description":"A block is inserted at the cursor.","user_visible_outcome":"A new heading node appears in the prose canvas at the caret position."}
   ]'::jsonb),

  ('flow.ribbon-tab-switch',
   'Switch ribbon tabs',
   'Move between the ribbon command groups.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The editor ribbon shows the Home tab by default."},
     {"step_kind":"action","action_kind":"click","description":"Click the Cite tab in the ribbon."},
     {"step_kind":"assert","description":"The ribbon shows the Cite command group.","user_visible_outcome":"The Cite tab is active and its citation commands are shown."},
     {"step_kind":"action","action_kind":"click","description":"Click the View tab in the ribbon."},
     {"step_kind":"assert","description":"The ribbon shows the View command group.","user_visible_outcome":"The View tab is active and its view commands are shown."}
   ]'::jsonb),

  ('flow.ribbon-format-bold',
   'Bold a selection from the ribbon',
   'Format selected text bold with the ribbon Bold button.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
     {"step_kind":"action","action_kind":"type","description":"Click into the canvas and type a word."},
     {"step_kind":"action","action_kind":"press","description":"Select the text with Ctrl+A."},
     {"step_kind":"action","action_kind":"click","description":"On the ribbon Home tab, click the Bold button."},
     {"step_kind":"assert","description":"The selection is formatted bold.","user_visible_outcome":"The selected word renders in bold."}
   ]'::jsonb),

  ('flow.outline-lists-headings',
   'The outline lists the document headings',
   'Open the outline and see every heading in the document.',
   '[
     {"step_kind":"setup","description":"Open a document that contains a few headings, in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready."},
     {"step_kind":"action","action_kind":"click","description":"Open the Outline tab in the right rail."},
     {"step_kind":"assert","description":"The outline lists every heading.","user_visible_outcome":"Each document heading appears as an entry in the outline, in document order."}
   ]'::jsonb),

  ('flow.outline-click-scrolls',
   'Clicking an outline entry scrolls the canvas',
   'Jump to a heading by clicking its outline entry.',
   '[
     {"step_kind":"setup","description":"Open a long document with headings in the Flow editor and open the Outline tab in the right rail.","user_visible_outcome":"The outline lists the document headings."},
     {"step_kind":"action","action_kind":"click","description":"Click an outline entry for a heading further down the document."},
     {"step_kind":"assert","description":"The canvas scrolls to that heading.","user_visible_outcome":"The target heading is scrolled into view in the prose canvas."}
   ]'::jsonb),

  ('flow.preview-renders',
   'The preview pane renders the document',
   'See the document rendered in the split preview pane.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor with the split preview shown.","user_visible_outcome":"The prose canvas and the preview pane sit side by side."},
     {"step_kind":"assert","description":"The preview pane renders the document.","user_visible_outcome":"The document content appears in the preview pane, mirroring the canvas."}
   ]'::jsonb),

  ('flow.preview-toggle',
   'Show and hide the preview pane',
   'Toggle the split preview pane on and off.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor in visual mode.","user_visible_outcome":"The prose canvas fills the editor; no preview pane is shown."},
     {"step_kind":"action","action_kind":"click","description":"Click the Toggle preview button in the top bar."},
     {"step_kind":"assert","description":"The preview pane appears beside the canvas.","user_visible_outcome":"The editor switches to split view with the LaTeX preview."},
     {"step_kind":"action","action_kind":"click","description":"Click the Toggle preview button again."},
     {"step_kind":"assert","description":"The preview pane is hidden.","user_visible_outcome":"The editor returns to the full-width visual canvas."}
   ]'::jsonb),

  ('flow.format-rail-opens',
   'The Format rail opens for the active block',
   'Open the Format rail to see controls for the block at the cursor.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready."},
     {"step_kind":"action","action_kind":"click","description":"Click into the canvas to place the cursor in a block."},
     {"step_kind":"action","action_kind":"click","description":"Open the Format tab in the right rail."},
     {"step_kind":"assert","description":"The Format rail opens for the active block.","user_visible_outcome":"The Format panel shows formatting controls for the block at the cursor."}
   ]'::jsonb),

  ('flow.command-palette-runs',
   'Run an action from the command palette',
   'Search for and run an action in the command palette.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready."},
     {"step_kind":"action","action_kind":"press","description":"Press Ctrl+K (or Cmd+K) to open the command palette."},
     {"step_kind":"action","action_kind":"type","description":"Type the name of an action, such as \"split view\"."},
     {"step_kind":"action","action_kind":"press","description":"Press Enter to run the highlighted action."},
     {"step_kind":"assert","description":"The palette closes and the action runs.","user_visible_outcome":"The command palette dismisses and the split-view preview pane appears."}
   ]'::jsonb),

  ('flow.activity-bar-switch-panel',
   'Switch the docked side panel from the activity bar',
   'Use the left activity bar to change the docked side panel.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The left activity bar is visible — Insert, Biblio, Formulas, Labels, Comments."},
     {"step_kind":"action","action_kind":"click","description":"Click the Biblio button in the left activity bar."},
     {"step_kind":"assert","description":"The docked side panel switches to Bibliography.","user_visible_outcome":"The side panel opens showing the Bibliography view."}
   ]'::jsonb),

  ('flow.comments-add-thread',
   'Add a comment thread in Flow mode',
   'Start a new comment thread from the Comments rail.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready."},
     {"step_kind":"action","action_kind":"click","description":"Open the Comments tab in the right rail."},
     {"step_kind":"action","action_kind":"type","description":"Type your note into the Add a comment composer."},
     {"step_kind":"action","action_kind":"click","description":"Click Send."},
     {"step_kind":"assert","description":"A new comment thread is created.","user_visible_outcome":"Your comment appears in the comments list."}
   ]'::jsonb),

  ('flow.formula-panel-inserts',
   'Insert a formula from the Formulas panel',
   'Insert a library formula into the canvas from the Formulas side panel.',
   '[
     {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready."},
     {"step_kind":"action","action_kind":"click","description":"Click the Formulas button in the left activity bar."},
     {"step_kind":"action","action_kind":"click","description":"In the Formulas side panel, open the Library tab."},
     {"step_kind":"action","action_kind":"click","description":"Click Insert on a formula from the library."},
     {"step_kind":"assert","description":"The formula is inserted into the canvas.","user_visible_outcome":"An equation block with the typeset formula appears in the prose canvas."}
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
    'flow.toggle-to-block', 'flow.toggle-to-flow', 'flow.toolbox-insert-at-cursor',
    'flow.ribbon-tab-switch', 'flow.ribbon-format-bold', 'flow.outline-lists-headings',
    'flow.outline-click-scrolls', 'flow.preview-renders', 'flow.preview-toggle',
    'flow.format-rail-opens', 'flow.command-palette-runs', 'flow.activity-bar-switch-panel',
    'flow.comments-add-thread', 'flow.formula-panel-inserts'
  );

DO $$
DECLARE n INT;
BEGIN
  SELECT count(*) INTO n
  FROM e2e.scenario s
  JOIN e2e.module m ON m.id = s.module_id
  WHERE m.slug = 'flow' AND s.detail_level = 'l2';
  RAISE NOTICE 'flow scenarios at L2 after batch 2: %', n;
END$$;

COMMIT;
