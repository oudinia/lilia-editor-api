-- Add version column to formulas table for tracking updates
-- Used by document/formula linking system to detect outdated blocks

ALTER TABLE formulas
  ADD COLUMN IF NOT EXISTS version integer NOT NULL DEFAULT 1;

-- All existing formulas start at version 1
UPDATE formulas SET version = 1 WHERE version IS NULL OR version = 0;
