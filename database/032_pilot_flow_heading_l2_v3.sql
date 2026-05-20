-- =====================================================================
--  Sprint 0 pilot — extend flow.heading.create-via-slash to 10 steps.
--
--  Adds version 3 (L2): the original 8 steps plus typing the heading
--  title (e.g. "Introduction") and asserting it shows. Feedback — a
--  heading tutorial should end with the heading actually carrying text.
--
--  Idempotent: guarded by NOT EXISTS on version_number = 3.
-- =====================================================================

BEGIN;

INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT s.id, 3, 'l2',
       'Heading: create via slash command',
       'Insert a heading in the Flow editor using the slash command, then give it a title.',
       '[
         {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
         {"step_kind":"action","action_kind":"click","description":"Click into an empty line in the canvas to place the cursor."},
         {"step_kind":"action","action_kind":"type","description":"Type a forward slash to summon the slash command menu."},
         {"step_kind":"assert","description":"The slash command menu opens at the cursor.","user_visible_outcome":"A floating menu of block types appears, anchored to the caret."},
         {"step_kind":"action","action_kind":"type","description":"Type the word heading to filter the menu."},
         {"step_kind":"assert","description":"The menu narrows to the Heading command.","user_visible_outcome":"Only the Heading entry and close matches remain listed."},
         {"step_kind":"action","action_kind":"press","description":"Press Enter, or click the Heading item, to insert the block."},
         {"step_kind":"assert","description":"A heading block replaces the line.","user_visible_outcome":"The line becomes a styled heading, with the cursor inside, ready for the title text."},
         {"step_kind":"action","action_kind":"type","description":"Type the heading title, for example Introduction."},
         {"step_kind":"assert","description":"The heading displays the typed title.","user_visible_outcome":"The line now reads as a large styled heading carrying your title."}
       ]'::jsonb,
       'llm_draft', 'claude-sprint0'
FROM e2e.scenario s
WHERE s.slug = 'flow.heading.create-via-slash'
  AND NOT EXISTS (
    SELECT 1 FROM e2e.scenario_version v
    WHERE v.scenario_id = s.id AND v.version_number = 3
  );

UPDATE e2e.scenario s
SET detail_level = 'l2',
    description = 'Insert a heading in the Flow editor using the slash command, then give it a title.',
    current_version_id = v.id,
    updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = s.id
  AND v.version_number = 3
  AND s.slug = 'flow.heading.create-via-slash';

DO $$
DECLARE steps INT;
BEGIN
  SELECT jsonb_array_length(v.steps) INTO steps
  FROM e2e.scenario sc JOIN e2e.scenario_version v ON v.id = sc.current_version_id
  WHERE sc.slug = 'flow.heading.create-via-slash';
  RAISE NOTICE 'pilot flow.heading.create-via-slash now at v3, steps=%', steps;
END$$;

COMMIT;
