-- =====================================================================
--  Sprint 0 pilot — promote flow.heading.create-via-slash to L2.
--
--  Adds a version-2 scenario_version (detail_level 'l2') with authored
--  steps and points the scenario at it. This is the vertical-slice
--  pilot for the Scenario System — see
--  lilia-docs/scenario-system/plan.md (Sprint 0). The broad L1->L2
--  promotion is Phase 2.
--
--  Idempotent: guarded by NOT EXISTS on version_number = 2.
-- =====================================================================

BEGIN;

INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT s.id, 2, 'l2',
       'Heading: create via slash command',
       'Insert a heading in the Flow editor using the slash command.',
       '[
         {"step_kind":"setup","description":"Open a document in the Flow editor.","user_visible_outcome":"The continuous-prose canvas is ready for input."},
         {"step_kind":"action","action_kind":"click","description":"Click into an empty line in the canvas to place the cursor."},
         {"step_kind":"action","action_kind":"type","description":"Type a forward slash to summon the slash command menu."},
         {"step_kind":"assert","description":"The slash command menu opens at the cursor.","user_visible_outcome":"A floating menu of block types appears, anchored to the caret."},
         {"step_kind":"action","action_kind":"type","description":"Type the word heading to filter the menu."},
         {"step_kind":"assert","description":"The menu narrows to the Heading command.","user_visible_outcome":"Only the Heading entry and close matches remain listed."},
         {"step_kind":"action","action_kind":"press","description":"Press Enter, or click the Heading item, to insert the block."},
         {"step_kind":"assert","description":"A heading block replaces the line.","user_visible_outcome":"The line becomes a styled heading with the cursor inside, ready for the title text."}
       ]'::jsonb,
       'llm_draft', 'claude-sprint0'
FROM e2e.scenario s
WHERE s.slug = 'flow.heading.create-via-slash'
  AND NOT EXISTS (
    SELECT 1 FROM e2e.scenario_version v
    WHERE v.scenario_id = s.id AND v.version_number = 2
  );

UPDATE e2e.scenario s
SET detail_level = 'l2',
    description = 'Insert a heading in the Flow editor using the slash command.',
    current_version_id = v.id,
    updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = s.id
  AND v.version_number = 2
  AND s.slug = 'flow.heading.create-via-slash';

DO $$
DECLARE lvl TEXT; steps INT;
BEGIN
  SELECT sc.detail_level,
         jsonb_array_length(v.steps)
    INTO lvl, steps
  FROM e2e.scenario sc
  JOIN e2e.scenario_version v ON v.id = sc.current_version_id
  WHERE sc.slug = 'flow.heading.create-via-slash';
  RAISE NOTICE 'pilot flow.heading.create-via-slash: detail_level=%, steps=%', lvl, steps;
END$$;

COMMIT;
