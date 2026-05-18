-- =====================================================================
--  Block-deep scenario seed — L1 stubs generated from the applicability
--  matrix in scripts/e2e/generate-block-scenarios.mjs (123 rows).
--
--  Idempotent: ON CONFLICT(slug) DO NOTHING for the scenario rows,
--  guarded NOT EXISTS for the version rows. Re-run safe.
--
--  Seeded scenarios start in review_state='draft'. Promote to
--  'approved' once they have an L2/L3 implementation, or to
--  'deprecated' if you decide to drop one.
-- =====================================================================

BEGIN;

WITH m AS (SELECT id, slug FROM e2e.module),
     s AS (SELECT id, module_id, slug FROM e2e.surface)
INSERT INTO e2e.scenario (
  slug, title, description, module_id, target_surface_id,
  criticality, detail_level, review_state, execution_mode,
  template, automation_content, milestone, created_by
)
SELECT v.slug,
       v.title,
       NULL,
       m.id,
       CASE WHEN v.surface_slug IS NULL THEN NULL
            ELSE (SELECT id FROM s WHERE module_id = m.id AND slug = v.surface_slug)
       END,
       v.criticality, 'l1', 'draft', 'integration',
       'standard', v.fingerprint, 'block-deep-seed-2026-05-18', 'claude-block-seed'
FROM (VALUES
  ('studio', 'block-type-menu', 'studio.paragraph.create-via-plus', 'Paragraph: create via + button', 'af916ffe4d2d2674', 'p0'),
  ('studio', 'slash-command-menu', 'studio.paragraph.create-via-slash', 'Paragraph: create via slash command', '860ac7340d1e6454', 'p0'),
  ('studio', 'block-card', 'studio.paragraph.edit-inline', 'Paragraph: edit inline', 'ad6c18f1497c4862', 'p0'),
  ('studio', 'slash-command-menu', 'studio.paragraph.convert-via-slash', 'Paragraph: convert via slash command', 'fa03c50085f54e72', 'p1'),
  ('studio', 'block-card', 'studio.paragraph.convert-via-kebab', 'Paragraph: convert via kebab menu', '8f8cf339a7731c11', 'p2'),
  ('studio', 'block-card', 'studio.paragraph.delete-via-kebab', 'Paragraph: delete via kebab menu', 'e597c11ddc44936e', 'p1'),
  ('studio', 'block-card', 'studio.paragraph.delete-via-keyboard', 'Paragraph: delete via keyboard shortcut', 'e5d3fcd10fa94df1', 'p2'),
  ('studio', 'block-card', 'studio.paragraph.reorder-via-drag', 'Paragraph: reorder via drag handle', 'f7d22e4a8e15783c', 'p1'),
  ('studio', 'block-canvas', 'studio.paragraph.render-preview', 'Paragraph: render in preview pane', '534955d9983523f1', 'p0'),
  ('export', 'export-dialog', 'studio.paragraph.export-to-latex', 'Paragraph: export to latex', '3be858c953e24929', 'p0'),
  ('export', 'export-dialog', 'studio.paragraph.export-to-markdown', 'Paragraph: export to markdown', 'f436ea3df4f4f2a6', 'p1'),
  ('studio', 'block-type-menu', 'studio.heading.create-via-plus', 'Heading: create via + button', 'e49e5f474ed9fa83', 'p0'),
  ('studio', 'slash-command-menu', 'studio.heading.create-via-slash', 'Heading: create via slash command', '2da299b8dc131a0f', 'p0'),
  ('studio', 'block-card', 'studio.heading.edit-inline', 'Heading: edit inline', 'c39c425c85ca023e', 'p0'),
  ('studio', 'slash-command-menu', 'studio.heading.convert-via-slash', 'Heading: convert via slash command', '3fde996b61f2fa0f', 'p1'),
  ('studio', 'block-card', 'studio.heading.convert-via-kebab', 'Heading: convert via kebab menu', 'd0755f2f7eb7b370', 'p2'),
  ('studio', 'block-card', 'studio.heading.delete-via-kebab', 'Heading: delete via kebab menu', 'a9a667f0de64bab7', 'p1'),
  ('studio', 'block-card', 'studio.heading.delete-via-keyboard', 'Heading: delete via keyboard shortcut', 'aae4cd17447d033f', 'p2'),
  ('studio', 'block-card', 'studio.heading.reorder-via-drag', 'Heading: reorder via drag handle', 'bd24abd04dfcb923', 'p1'),
  ('studio', 'block-canvas', 'studio.heading.render-preview', 'Heading: render in preview pane', 'f442776641c737ab', 'p0'),
  ('export', 'export-dialog', 'studio.heading.export-to-latex', 'Heading: export to latex', '9d7c7466c5cbd198', 'p0'),
  ('export', 'export-dialog', 'studio.heading.export-to-markdown', 'Heading: export to markdown', '70a84e1583edab3e', 'p1'),
  ('studio', 'block-type-menu', 'studio.list.create-via-plus', 'List: create via + button', '62b50423b032021b', 'p0'),
  ('studio', 'slash-command-menu', 'studio.list.create-via-slash', 'List: create via slash command', '7be845d9677400ab', 'p0'),
  ('studio', 'block-card', 'studio.list.edit-inline', 'List: edit inline', 'b17065814f99e1d9', 'p0'),
  ('studio', 'slash-command-menu', 'studio.list.convert-via-slash', 'List: convert via slash command', 'ecab6301466295f2', 'p1'),
  ('studio', 'block-card', 'studio.list.convert-via-kebab', 'List: convert via kebab menu', '7ff0b57fb766aadf', 'p2'),
  ('studio', 'block-card', 'studio.list.delete-via-kebab', 'List: delete via kebab menu', '8846e8efc7204594', 'p1'),
  ('studio', 'block-card', 'studio.list.delete-via-keyboard', 'List: delete via keyboard shortcut', 'b22be595f9d6ac13', 'p2'),
  ('studio', 'block-card', 'studio.list.reorder-via-drag', 'List: reorder via drag handle', 'add0fe0971052807', 'p1'),
  ('studio', 'block-canvas', 'studio.list.render-preview', 'List: render in preview pane', 'dc2fb77ce9340a13', 'p0'),
  ('export', 'export-dialog', 'studio.list.export-to-latex', 'List: export to latex', 'c9ff7a067b755306', 'p0'),
  ('export', 'export-dialog', 'studio.list.export-to-markdown', 'List: export to markdown', '4550dfa34b4d6e16', 'p1'),
  ('studio', 'block-type-menu', 'studio.blockquote.create-via-plus', 'Block quote: create via + button', '04ab0068bef11b33', 'p0'),
  ('studio', 'slash-command-menu', 'studio.blockquote.create-via-slash', 'Block quote: create via slash command', 'a777c4848797d8e0', 'p0'),
  ('studio', 'block-card', 'studio.blockquote.edit-inline', 'Block quote: edit inline', 'c7d86b2242840890', 'p0'),
  ('studio', 'slash-command-menu', 'studio.blockquote.convert-via-slash', 'Block quote: convert via slash command', 'b0c582c5cb5be36e', 'p1'),
  ('studio', 'block-card', 'studio.blockquote.convert-via-kebab', 'Block quote: convert via kebab menu', 'e1617d015a2e4531', 'p2'),
  ('studio', 'block-card', 'studio.blockquote.delete-via-kebab', 'Block quote: delete via kebab menu', 'bab5703a632ba1ec', 'p1'),
  ('studio', 'block-card', 'studio.blockquote.delete-via-keyboard', 'Block quote: delete via keyboard shortcut', '340b65cea1d85be3', 'p2'),
  ('studio', 'block-card', 'studio.blockquote.reorder-via-drag', 'Block quote: reorder via drag handle', 'd3ea6a42bcd41fac', 'p1'),
  ('studio', 'block-canvas', 'studio.blockquote.render-preview', 'Block quote: render in preview pane', '08c05410ee07d133', 'p0'),
  ('export', 'export-dialog', 'studio.blockquote.export-to-latex', 'Block quote: export to latex', '9f3ad4d2cc280b86', 'p0'),
  ('export', 'export-dialog', 'studio.blockquote.export-to-markdown', 'Block quote: export to markdown', '33394ed878e43d4f', 'p1'),
  ('studio', 'block-type-menu', 'studio.theorem.create-via-plus', 'Theorem: create via + button', '94fb3b6ef00b5797', 'p0'),
  ('studio', 'slash-command-menu', 'studio.theorem.create-via-slash', 'Theorem: create via slash command', '60274de3c9c47b57', 'p0'),
  ('studio', 'block-card', 'studio.theorem.edit-inline', 'Theorem: edit inline', 'a74ed8817320e58c', 'p0'),
  ('studio', 'block-settings-drawer', 'studio.theorem.edit-via-drawer', 'Theorem: edit via settings drawer', '5858afeccab037df', 'p1'),
  ('studio', 'slash-command-menu', 'studio.theorem.convert-via-slash', 'Theorem: convert via slash command', '2126bfc8ab2f9b37', 'p1'),
  ('studio', 'block-card', 'studio.theorem.convert-via-kebab', 'Theorem: convert via kebab menu', '3a6b95fb22da9951', 'p2'),
  ('studio', 'block-card', 'studio.theorem.delete-via-kebab', 'Theorem: delete via kebab menu', '4937d53f8339d783', 'p1'),
  ('studio', 'block-card', 'studio.theorem.delete-via-keyboard', 'Theorem: delete via keyboard shortcut', 'dd19a97995e0c588', 'p2'),
  ('studio', 'block-card', 'studio.theorem.reorder-via-drag', 'Theorem: reorder via drag handle', 'cedf3ff80c194fe3', 'p1'),
  ('studio', 'block-canvas', 'studio.theorem.render-preview', 'Theorem: render in preview pane', 'c69a96ef2fc4441e', 'p0'),
  ('export', 'export-dialog', 'studio.theorem.export-to-latex', 'Theorem: export to latex', '7e047c8ef5bb45ae', 'p0'),
  ('export', 'export-dialog', 'studio.theorem.export-to-markdown', 'Theorem: export to markdown', '2fa68571f6519eba', 'p1'),
  ('studio', 'block-type-menu', 'studio.abstract.create-via-plus', 'Abstract: create via + button', '9c915d2780b67767', 'p0'),
  ('studio', 'slash-command-menu', 'studio.abstract.create-via-slash', 'Abstract: create via slash command', '0b8ac05d1d8f4ec5', 'p0'),
  ('studio', 'block-card', 'studio.abstract.edit-inline', 'Abstract: edit inline', '7ed845a05445e59d', 'p0'),
  ('studio', 'slash-command-menu', 'studio.abstract.convert-via-slash', 'Abstract: convert via slash command', 'e5bc9ce6ff02ca3f', 'p1'),
  ('studio', 'block-card', 'studio.abstract.convert-via-kebab', 'Abstract: convert via kebab menu', 'e8920718b51af48b', 'p2'),
  ('studio', 'block-card', 'studio.abstract.delete-via-kebab', 'Abstract: delete via kebab menu', '3cec5391210fb91e', 'p1'),
  ('studio', 'block-card', 'studio.abstract.delete-via-keyboard', 'Abstract: delete via keyboard shortcut', '70533342b30eb66d', 'p2'),
  ('studio', 'block-card', 'studio.abstract.reorder-via-drag', 'Abstract: reorder via drag handle', '9d9bf90ac7e1d12f', 'p1'),
  ('studio', 'block-canvas', 'studio.abstract.render-preview', 'Abstract: render in preview pane', '87d35096b2403946', 'p0'),
  ('export', 'export-dialog', 'studio.abstract.export-to-latex', 'Abstract: export to latex', '1e18afeed7c8f641', 'p0'),
  ('export', 'export-dialog', 'studio.abstract.export-to-markdown', 'Abstract: export to markdown', 'acb559c6f6c5c814', 'p1'),
  ('studio', 'block-type-menu', 'studio.figure.create-via-plus', 'Figure: create via + button', '56a3119d7cbb6715', 'p0'),
  ('studio', 'slash-command-menu', 'studio.figure.create-via-slash', 'Figure: create via slash command', '873618112d85d5b4', 'p0'),
  ('studio', 'block-canvas', 'studio.figure.create-via-drag-drop', 'Figure: create via drag and drop', '5be8e522f23569dd', 'p1'),
  ('studio', 'block-settings-drawer', 'studio.figure.edit-via-drawer', 'Figure: edit via settings drawer', '1ef346a1e12572a0', 'p1'),
  ('studio', 'block-card', 'studio.figure.delete-via-kebab', 'Figure: delete via kebab menu', '0fcc4123590b120c', 'p1'),
  ('studio', 'block-card', 'studio.figure.reorder-via-drag', 'Figure: reorder via drag handle', 'e30f7a238bb5012c', 'p1'),
  ('studio', 'block-canvas', 'studio.figure.render-preview', 'Figure: render in preview pane', '9be88964e6e282b6', 'p0'),
  ('export', 'export-dialog', 'studio.figure.export-to-latex', 'Figure: export to latex', 'f65bb30109d847b5', 'p0'),
  ('export', 'export-dialog', 'studio.figure.export-to-markdown', 'Figure: export to markdown', '68c5cd3c5a577b6d', 'p1'),
  ('studio', 'block-type-menu', 'studio.equation.create-via-plus', 'Equation: create via + button', 'f5b5ecd191eaf449', 'p0'),
  ('studio', 'slash-command-menu', 'studio.equation.create-via-slash', 'Equation: create via slash command', '06ae53c4253136e6', 'p0'),
  ('studio', 'formula-editor', 'studio.equation.edit-via-modal', 'Equation: edit via formula editor', 'a2e6c08e6cdad225', 'p0'),
  ('studio', 'block-card', 'studio.equation.delete-via-kebab', 'Equation: delete via kebab menu', '9a75c60d3d50a971', 'p1'),
  ('studio', 'block-card', 'studio.equation.reorder-via-drag', 'Equation: reorder via drag handle', '59735d77a19ad183', 'p1'),
  ('studio', 'block-canvas', 'studio.equation.render-preview', 'Equation: render in preview pane', '3f94f790b473662f', 'p0'),
  ('export', 'export-dialog', 'studio.equation.export-to-latex', 'Equation: export to latex', 'a6fc69c91f25f78c', 'p0'),
  ('export', 'export-dialog', 'studio.equation.export-to-markdown', 'Equation: export to markdown', '909394d15084712e', 'p1'),
  ('studio', 'block-type-menu', 'studio.code.create-via-plus', 'Code: create via + button', 'c5f6a96d1ad891a2', 'p0'),
  ('studio', 'slash-command-menu', 'studio.code.create-via-slash', 'Code: create via slash command', 'df55023623aee6f2', 'p0'),
  ('studio', 'block-card', 'studio.code.edit-inline', 'Code: edit inline', '0fb63983166944d8', 'p0'),
  ('studio', 'block-settings-drawer', 'studio.code.edit-via-drawer', 'Code: edit via settings drawer', 'eb024316d096de0f', 'p1'),
  ('studio', 'block-card', 'studio.code.delete-via-kebab', 'Code: delete via kebab menu', 'b927c5b405ddb34d', 'p1'),
  ('studio', 'block-card', 'studio.code.delete-via-keyboard', 'Code: delete via keyboard shortcut', '06dc0440fe8bdfe6', 'p2'),
  ('studio', 'block-card', 'studio.code.reorder-via-drag', 'Code: reorder via drag handle', 'f03cab23fdf3e168', 'p1'),
  ('studio', 'block-canvas', 'studio.code.render-preview', 'Code: render in preview pane', 'f50b1b83cae98218', 'p0'),
  ('export', 'export-dialog', 'studio.code.export-to-latex', 'Code: export to latex', 'f8d21883aaa6be7f', 'p0'),
  ('export', 'export-dialog', 'studio.code.export-to-markdown', 'Code: export to markdown', '2dc307edfce43654', 'p1'),
  ('studio', 'block-type-menu', 'studio.table.create-via-plus', 'Table: create via + button', '8a47fa929d70c5ab', 'p0'),
  ('studio', 'slash-command-menu', 'studio.table.create-via-slash', 'Table: create via slash command', 'db6eb6c8496b3a46', 'p0'),
  ('studio', 'block-settings-drawer', 'studio.table.edit-via-drawer', 'Table: edit via settings drawer', 'feaec2d69e8eb71e', 'p1'),
  ('studio', 'block-card', 'studio.table.delete-via-kebab', 'Table: delete via kebab menu', '9d5b94dfc34bc681', 'p1'),
  ('studio', 'block-card', 'studio.table.reorder-via-drag', 'Table: reorder via drag handle', '5f745d591c9efed6', 'p1'),
  ('studio', 'block-canvas', 'studio.table.render-preview', 'Table: render in preview pane', 'ab9661ff6612baed', 'p0'),
  ('export', 'export-dialog', 'studio.table.export-to-latex', 'Table: export to latex', '60b5743009f99c2e', 'p0'),
  ('export', 'export-dialog', 'studio.table.export-to-markdown', 'Table: export to markdown', '17be6ad0b58d4e4d', 'p1'),
  ('studio', 'block-type-menu', 'studio.bibliography.create-via-plus', 'Bibliography: create via + button', '492224e2649220d0', 'p0'),
  ('studio', 'slash-command-menu', 'studio.bibliography.create-via-slash', 'Bibliography: create via slash command', '1da6cc5945ed0288', 'p0'),
  ('studio', 'block-card', 'studio.bibliography.delete-via-kebab', 'Bibliography: delete via kebab menu', '5d3782a12984650e', 'p1'),
  ('studio', 'block-card', 'studio.bibliography.reorder-via-drag', 'Bibliography: reorder via drag handle', 'b6a751ff4565697f', 'p1'),
  ('studio', 'block-canvas', 'studio.bibliography.render-preview', 'Bibliography: render in preview pane', '9191e03b0ac6539c', 'p0'),
  ('export', 'export-dialog', 'studio.bibliography.export-to-latex', 'Bibliography: export to latex', '8493334888d04373', 'p0'),
  ('export', 'export-dialog', 'studio.bibliography.export-to-markdown', 'Bibliography: export to markdown', '594a70571e1d9eba', 'p1'),
  ('studio', 'block-type-menu', 'studio.tableOfContents.create-via-plus', 'Table of contents: create via + button', '946cfa364202f040', 'p0'),
  ('studio', 'slash-command-menu', 'studio.tableOfContents.create-via-slash', 'Table of contents: create via slash command', '646371f00f65d32f', 'p0'),
  ('studio', 'block-card', 'studio.tableOfContents.delete-via-kebab', 'Table of contents: delete via kebab menu', '7c6c74292a761f77', 'p1'),
  ('studio', 'block-card', 'studio.tableOfContents.reorder-via-drag', 'Table of contents: reorder via drag handle', '0de70366dd7b3320', 'p1'),
  ('studio', 'block-canvas', 'studio.tableOfContents.render-preview', 'Table of contents: render in preview pane', 'd7cea1d3a8b9deea', 'p0'),
  ('export', 'export-dialog', 'studio.tableOfContents.export-to-latex', 'Table of contents: export to latex', 'c48fd760eb323b00', 'p0'),
  ('export', 'export-dialog', 'studio.tableOfContents.export-to-markdown', 'Table of contents: export to markdown', 'b6af2502940503fb', 'p1'),
  ('studio', 'block-type-menu', 'studio.pageBreak.create-via-plus', 'Page break: create via + button', 'f659cb24def46f6d', 'p0'),
  ('studio', 'slash-command-menu', 'studio.pageBreak.create-via-slash', 'Page break: create via slash command', 'e95c33b80ac754e2', 'p0'),
  ('studio', 'block-card', 'studio.pageBreak.delete-via-kebab', 'Page break: delete via kebab menu', '34f4a5165a4cb172', 'p1'),
  ('studio', 'block-card', 'studio.pageBreak.reorder-via-drag', 'Page break: reorder via drag handle', '3198b17d18508200', 'p1'),
  ('studio', 'block-canvas', 'studio.pageBreak.render-preview', 'Page break: render in preview pane', '57c045e9dc338a1d', 'p0'),
  ('export', 'export-dialog', 'studio.pageBreak.export-to-latex', 'Page break: export to latex', 'b1941fc97cdf6254', 'p0'),
  ('export', 'export-dialog', 'studio.pageBreak.export-to-markdown', 'Page break: export to markdown', '314d4729ae9b7214', 'p1')
) AS v(module_slug, surface_slug, slug, title, fingerprint, criticality)
JOIN m ON m.slug = v.module_slug
ON CONFLICT (slug) DO NOTHING;

-- Insert version 1 (L1, generated) for every seeded scenario.
INSERT INTO e2e.scenario_version (
  scenario_id, version_number, detail_level, title, description,
  steps, generation_provenance, generated_by
)
SELECT
  sc.id, 1, 'l1', sc.title, sc.description,
  '[]'::jsonb, 'llm_draft', 'claude-block-seed'
FROM e2e.scenario sc
WHERE sc.milestone = 'block-deep-seed-2026-05-18'
  AND NOT EXISTS (
    SELECT 1 FROM e2e.scenario_version v
    WHERE v.scenario_id = sc.id AND v.version_number = 1
  );

-- Wire current_version_id pointer.
UPDATE e2e.scenario sc
SET current_version_id = v.id, updated_at = NOW()
FROM e2e.scenario_version v
WHERE v.scenario_id = sc.id
  AND v.version_number = 1
  AND sc.current_version_id IS DISTINCT FROM v.id
  AND sc.milestone = 'block-deep-seed-2026-05-18';

DO $$
DECLARE c INTEGER;
BEGIN
  SELECT COUNT(*) INTO c FROM e2e.scenario
    WHERE milestone = 'block-deep-seed-2026-05-18';
  RAISE NOTICE 'block-deep scenarios in DB: %', c;
END$$;

COMMIT;
