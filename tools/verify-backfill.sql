-- Verifies the jsonb expression in 20260731114747_BackfillEquationSource
-- against a scratch table, so the statement is exercised on a real Postgres
-- before it is ever pointed at real rows.
--
--   node ../tex-signal/scripts/neon-http.mjs --file tools/verify-backfill.sql
--
-- Self-contained: creates its own schema and drops it again.

CREATE SCHEMA IF NOT EXISTS backfill_check;

DROP TABLE IF EXISTS backfill_check.blocks;

CREATE TABLE backfill_check.blocks (
  id      int generated always as identity primary key,
  note    text,
  type    text,
  content jsonb
);

INSERT INTO backfill_check.blocks (note, type, content) VALUES
  ('legacy equation - must gain source+notation', 'equation', '{"latex":"E=mc^2","mode":"display"}'),
  ('mixed case type - must still match',          'Equation', '{"latex":"a^2+b^2"}'),
  ('already migrated - must not change',          'equation', '{"latex":"x","source":"y","notation":"latex"}'),
  ('explicit notation - must be preserved',       'equation', '{"latex":"x","notation":"mathml"}'),
  ('empty latex - nothing to copy',               'equation', '{"latex":"","mode":"display"}'),
  ('no latex at all - freshly inserted',          'equation', '{"mode":"display"}'),
  ('not an equation - must be untouched',         'paragraph','{"latex":"not really math"}'),
  ('non-object content - must not error',         'equation', '"just a string"');

UPDATE backfill_check.blocks
SET content = content
    || jsonb_build_object('source', content->>'latex')
    || jsonb_build_object('notation', COALESCE(content->>'notation', 'latex'))
WHERE lower(type) = 'equation'
  AND jsonb_typeof(content) = 'object'
  AND COALESCE(content->>'latex', '') <> ''
  AND COALESCE(content->>'source', '') = '';

-- Every row states what it expects, so the output is a pass/fail list rather
-- than a dump to eyeball.
-- Aggregated to one row on purpose: the HTTP runner truncates printed rows,
-- so a per-row listing gets cut off and a failure can hide past the cut.
SELECT
  count(*) FILTER (WHERE verdict = 'PASS') AS passed,
  count(*) FILTER (WHERE verdict = 'FAIL') AS failed,
  coalesce(string_agg(note, ' | ') FILTER (WHERE verdict = 'FAIL'), '-') AS failing
FROM (
SELECT
  note,
  CASE
    WHEN note LIKE 'legacy%'           AND content->>'source' = 'E=mc^2'   AND content->>'notation' = 'latex'  THEN 'PASS'
    WHEN note LIKE 'mixed case%'       AND content->>'source' = 'a^2+b^2'  AND content->>'notation' = 'latex'  THEN 'PASS'
    WHEN note LIKE 'already migrated%' AND content->>'source' = 'y'                                            THEN 'PASS'
    WHEN note LIKE 'explicit notation%' AND content->>'source' = 'x'       AND content->>'notation' = 'mathml' THEN 'PASS'
    WHEN note LIKE 'empty latex%'      AND content->>'source' IS NULL                                          THEN 'PASS'
    WHEN note LIKE 'no latex%'         AND content->>'source' IS NULL                                          THEN 'PASS'
    WHEN note LIKE 'not an equation%'  AND content->>'source' IS NULL                                          THEN 'PASS'
    WHEN note LIKE 'non-object%'       AND content = '"just a string"'::jsonb                                  THEN 'PASS'
    ELSE 'FAIL'
  END AS verdict
FROM backfill_check.blocks
) checked;

DROP SCHEMA backfill_check CASCADE;
