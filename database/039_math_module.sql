-- =====================================================================
--  Scenario System — math editor module + L2 scenarios.
--
--  Adds a new "math" module and seeds 15 L2 scenarios covering the
--  Lilia Math editor surface (token model, keyboard layer, drawer
--  tabs, slash palette, responsive fallback).
--
--  Idempotent: module + scenarios guarded by NOT EXISTS on their
--  slugs.
-- =====================================================================

BEGIN;

INSERT INTO e2e.module (id, slug, name, description, owner, criticality, is_active)
SELECT gen_random_uuid(), 'math', 'Math editor',
       'Lilia Math — token-based WYSIWYG equation editor (tablet + desktop).',
       'claude-design', 'p1', true
WHERE NOT EXISTS (SELECT 1 FROM e2e.module WHERE slug = 'math');

WITH cat (slug, criticality, title, descr, automation_content, steps) AS (
  VALUES
  ('math.editor-mounts', 'p1', 'Math editor mounts on equation block edit', 'Opening an equation block in tablet/desktop mode mounts the new Math editor surface (token-based, drawer to the right).', '77b9be8f553401d1', '[{"step_kind":"setup","description":"Open a document containing at least one equation block.","user_visible_outcome":"The studio loads with its block cards."},{"step_kind":"action","action_kind":"click","description":"Click the equation block card to open its editor."},{"step_kind":"assert","description":"The Lilia Math editor mounts.","user_visible_outcome":"The \"EDITING\" pill, equation block, and Symbols drawer are visible."}]'::jsonb),
  ('math.type-letter-inserts-atom', 'p0', 'Typing a letter inserts an atom', 'Pressing a letter key with no field focused appends an italic letter token to the equation.', '11a6c6f59dab6e5f', '[{"step_kind":"setup","description":"Open the math editor with an empty equation."},{"step_kind":"action","action_kind":"type","description":"Press the \"a\" key."},{"step_kind":"assert","description":"A new italic \"a\" token appears at the caret position.","user_visible_outcome":"The footer token count increments."}]'::jsonb),
  ('math.shift-enter-newline', 'p1', 'Shift+Enter inserts a hard line break', 'Shift+Enter inserts a `newline` token that drops the next token onto a new visual row inside the same block.', 'ad0d491c17a6bfb3', '[{"step_kind":"setup","description":"Open the math editor with at least one token."},{"step_kind":"action","action_kind":"press","description":"Press Shift+Enter."},{"step_kind":"assert","description":"A new line begins after the caret.","user_visible_outcome":"Subsequent input renders on a second row of the rendered equation."}]'::jsonb),
  ('math.ampersand-align', 'p1', 'Ampersand inserts an alignment marker', 'Typing & inserts an `align` token; once a line has one, equals signs across rows line up in the 2-column grid.', '0c345d125c0992e1', '[{"step_kind":"setup","description":"Open the math editor on an aligned-sample equation."},{"step_kind":"action","action_kind":"type","description":"Press the & key in a line that has no align marker."},{"step_kind":"assert","description":"The line splits into a right-aligned LHS and left-aligned RHS.","user_visible_outcome":"Equals signs line up across rows."}]'::jsonb),
  ('math.backspace-deletes', 'p0', 'Backspace deletes the token before the caret', 'Backspace removes the token at index cursor-1 and moves the caret left.', '91b776f62215daaf', '[{"step_kind":"setup","description":"Open the math editor with at least two tokens."},{"step_kind":"action","action_kind":"press","description":"Press Backspace."},{"step_kind":"assert","description":"The token before the caret is removed.","user_visible_outcome":"Token count decrements and the caret moves one position left."}]'::jsonb),
  ('math.slash-palette-opens', 'p1', 'Slash opens the command palette', 'Typing \ (or /) at the caret opens the slash command palette anchored under the equation block.', 'e52fff938b4a2f2f', '[{"step_kind":"setup","description":"Open the math editor."},{"step_kind":"action","action_kind":"type","description":"Press the \\ key."},{"step_kind":"assert","description":"The slash palette opens with a search input.","user_visible_outcome":"Results render with category-coloured glyphs + LaTeX commands."}]'::jsonb),
  ('math.slash-palette-inserts', 'p0', 'Slash palette inserts at caret', 'Typing a query into the palette and pressing Enter inserts the highlighted command at the caret.', '834823fba37e303f', '[{"step_kind":"setup","description":"Open the math editor."},{"step_kind":"action","action_kind":"type","description":"Press \\ to open the palette, then type \"alpha\"."},{"step_kind":"action","action_kind":"press","description":"Press Enter."},{"step_kind":"assert","description":"An α token appears at the caret.","user_visible_outcome":"The palette closes; the LaTeX preview shows \\alpha."}]'::jsonb),
  ('math.tab-cycles-tabs', 'p2', 'Tab cycles drawer tabs', 'Tab key (outside a text field) advances through Symbols → Structures → Source.', '9eefb3f2817504fc', '[{"step_kind":"setup","description":"Open the math editor; the Symbols tab is active."},{"step_kind":"action","action_kind":"press","description":"Press Tab."},{"step_kind":"assert","description":"The Structures tab becomes active.","user_visible_outcome":"Press Tab twice more — the Source tab shows the LaTeX preview."}]'::jsonb),
  ('math.symbols-grid-no-x-scroll', 'p1', 'Symbols grid never scrolls horizontally', 'No category (Greek/Ops/Rel/Big ops/Arrows/Functions/Logic/Sets) produces a horizontal scrollbar in the grid panel.', '5a71802d9a90a431', '[{"step_kind":"setup","description":"Open the math editor with the side drawer visible."},{"step_kind":"action","action_kind":"click","description":"Click each category in the left column in turn."},{"step_kind":"assert","description":"Every grid wraps to fit the panel.","user_visible_outcome":"The panel never grows a horizontal scrollbar at any drawer width."}]'::jsonb),
  ('math.structures-tab-tile-inserts', 'p1', 'Structures tile inserts a compound token', 'Clicking a tile in the Structures tab inserts the corresponding compound structure at the caret.', '63bd4d7b815e822f', '[{"step_kind":"setup","description":"Open the math editor and switch to the Structures tab."},{"step_kind":"action","action_kind":"click","description":"Click the \"Fraction\" tile."},{"step_kind":"assert","description":"A fraction structure appears at the caret.","user_visible_outcome":"Both placeholder slots are visible in the rendered equation."}]'::jsonb),
  ('math.source-tab-shows-latex', 'p0', 'Source tab renders live LaTeX', 'The Source tab shows a syntax-coloured LaTeX representation of the current equation that updates as tokens change.', 'cbd2bb10f9e5052b', '[{"step_kind":"setup","description":"Open the math editor with a non-empty equation."},{"step_kind":"action","action_kind":"click","description":"Switch to the Source tab."},{"step_kind":"assert","description":"A wrapped \\begin{equation} block renders with line numbers.","user_visible_outcome":"LaTeX commands are coloured indigo and update as the equation changes."}]'::jsonb),
  ('math.source-copy-button', 'p2', 'Copy button writes LaTeX to clipboard', 'The Source-tab Copy button writes the wrapped LaTeX representation to the clipboard.', 'f4c2456ee21aaa94', '[{"step_kind":"setup","description":"Open the math editor and switch to the Source tab."},{"step_kind":"action","action_kind":"click","description":"Click the Copy button."},{"step_kind":"assert","description":"The button label flips to \"Copied\" briefly.","user_visible_outcome":"The clipboard contains the wrapped \\begin{equation}…\\end{equation} block."}]'::jsonb),
  ('math.responsive-drawer-folds', 'p2', 'Drawer folds below 980px viewport', 'Resizing the viewport below 980px auto-folds the side drawer into a block-inline drawer below the equation.', '0eebdb2f6dbf0aea', '[{"step_kind":"setup","description":"Open the math editor on a wide viewport (side drawer visible)."},{"step_kind":"action","action_kind":"resize","description":"Resize the viewport to ~900px wide."},{"step_kind":"assert","description":"The drawer renders below the equation block, not beside it.","user_visible_outcome":"The equation block does not shrink below ~600px."}]'::jsonb),
  ('math.done-emits-latex', 'p1', 'Done button emits the current LaTeX', 'Clicking Done in the header fires the host''s onDone callback with the current LaTeX string.', '459a1418158710ca', '[{"step_kind":"setup","description":"Open the math editor with a non-empty equation."},{"step_kind":"action","action_kind":"click","description":"Click the Done button."},{"step_kind":"assert","description":"The host receives the equation''s LaTeX.","user_visible_outcome":"The editor closes if the host wires the callback to a dismiss action."}]'::jsonb),
  ('math.caret-click-positions', 'p1', 'Clicking a gap moves the caret', 'Clicking any caret slot between two tokens moves the cursor to that index.', 'e05e3ef7aa4141d0', '[{"step_kind":"setup","description":"Open the math editor with at least three tokens."},{"step_kind":"action","action_kind":"click","description":"Click the gap between the first and second token."},{"step_kind":"assert","description":"The blinking caret appears at index 1.","user_visible_outcome":"The footer shows pos 1."}]'::jsonb)
),
ins AS (
  INSERT INTO e2e.scenario (
    id, slug, title, description, module_id, criticality, detail_level,
    review_state, execution_mode, template, automation_content,
    is_deleted, created_at, updated_at, created_by
  )
  SELECT gen_random_uuid(), c.slug, c.title, c.descr, m.id, c.criticality, 'l2',
         'draft', 'integration', 'standard', c.automation_content,
         false, NOW(), NOW(), 'claude-math-seed'
  FROM cat c
  JOIN e2e.module m ON m.slug = 'math'
  WHERE NOT EXISTS (SELECT 1 FROM e2e.scenario s WHERE s.slug = c.slug)
  RETURNING id, slug
)
INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT i.id, 1, 'l2', c.title, c.descr, c.steps, 'llm_draft', 'claude-math-seed'
FROM ins i
JOIN cat c ON c.slug = i.slug;

UPDATE e2e.scenario s
SET current_version_id = v.id, updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = s.id
  AND v.version_number = 1
  AND s.created_by = 'claude-math-seed'
  AND s.current_version_id IS NULL;

DO $$
DECLARE
  n_math INT;
BEGIN
  SELECT count(*) INTO n_math FROM e2e.scenario s
    JOIN e2e.module m ON m.id = s.module_id
    WHERE m.slug = 'math' AND NOT s.is_deleted;
  RAISE NOTICE 'math module scenarios: %', n_math;
END$$;

COMMIT;
