-- =====================================================================
--  Scenario System — one-off backfill of L3 detail_level.
--
--  Until this point, RecordResultAsync wrote scenario_result rows but
--  never bumped the scenario's high-water mark. So 17 scenarios that
--  have at least one green L3 run on record still sat at L2 in the
--  catalogue. This script promotes them — same rule the runtime now
--  applies on every new green result:
--
--      pass(l3) on a scenario whose current detail_level < l3
--      ⇒ scenario.detail_level = 'l3'
--        + current_version.detail_level = 'l3'
--
--  Run-once, idempotent (re-running is a no-op once everyone's at L3).
-- =====================================================================

BEGIN;

WITH promoted AS (
  SELECT DISTINCT s.id AS scenario_id, s.current_version_id
  FROM e2e.scenario s
  JOIN e2e.scenario_result r ON r.scenario_id = s.id
  WHERE r.outcome = 'pass'
    AND r.detail_level_run = 'l3'
    AND s.detail_level <> 'l3'
    AND NOT s.is_deleted
)
UPDATE e2e.scenario_version v
SET detail_level = 'l3'
FROM promoted p
WHERE v.id = p.current_version_id
  AND v.detail_level <> 'l3';

WITH promoted AS (
  SELECT DISTINCT s.id AS scenario_id
  FROM e2e.scenario s
  JOIN e2e.scenario_result r ON r.scenario_id = s.id
  WHERE r.outcome = 'pass'
    AND r.detail_level_run = 'l3'
    AND s.detail_level <> 'l3'
    AND NOT s.is_deleted
)
UPDATE e2e.scenario s
SET detail_level = 'l3',
    updated_at   = NOW()
FROM promoted p
WHERE s.id = p.scenario_id
  AND s.detail_level <> 'l3';

DO $$
DECLARE
  n_l3 INT;
BEGIN
  SELECT count(*) INTO n_l3 FROM e2e.scenario WHERE detail_level = 'l3' AND NOT is_deleted;
  RAISE NOTICE 'scenarios at L3 after backfill: %', n_l3;
END$$;

COMMIT;
