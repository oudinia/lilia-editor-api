using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Surfaces LaTeX size and alignment kernel commands in the editor's
    /// insertion panel + ⌘K + slash menu by promoting them out of
    /// 'unsupported' coverage and giving them brace-grouping templates.
    ///
    /// The parser still strips bare \Huge / \centering on import (those
    /// are pre-launch behavior we keep for clean visual display), but
    /// users who *want* to insert size/alignment commands from the
    /// visual editor now have a path. Inserted text uses {\Huge text}
    /// brace-grouping so size scope is paragraph-local — round-trips on
    /// export and is valid LaTeX.
    ///
    /// Migration is idempotent on UPDATE and uses ON CONFLICT for the
    /// INSERT block.
    /// </summary>
    /// <inheritdoc />
    public partial class SeedSizeAndAlignmentInsertions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- ── Demote misclassified env rows so they stop surfacing ─────
                -- \begin{Huge}…\end{Huge} isn't valid LaTeX; the env-kind
                -- rows here were a catalog seed mistake. They appeared in
                -- the InsertionsPanel as \begin{Huge}…\end{Huge} which is
                -- worse than not appearing at all.
                UPDATE latex_tokens SET coverage_level = 'unsupported'
                  WHERE kind = 'environment' AND package_slug IS NULL
                    AND name IN ('Huge','huge','LARGE','Large','large',
                                 'normalsize','small','footnotesize',
                                 'scriptsize','tiny','raggedright','raggedleft');

                -- ── Promote existing command rows to 'shimmed' + template ────
                -- These were 'unsupported' so they were filtered out of the
                -- insert surfaces. Now they appear with proper templates.
                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'font',
                      insert_template = '{\Huge |SELECTION|}'
                  WHERE name = 'Huge' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'font',
                      insert_template = '{\normalsize |SELECTION|}'
                  WHERE name = 'normalsize' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'layout',
                      notes = 'Center-align the following text or selection',
                      insert_template = '\centering '
                  WHERE name = 'centering' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'layout',
                      notes = 'Right-align disabled / left-justified text',
                      insert_template = '\raggedright '
                  WHERE name = 'raggedright' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'layout',
                      notes = 'Right-align text',
                      insert_template = '\raggedleft '
                  WHERE name = 'raggedleft' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens
                  SET coverage_level = 'shimmed',
                      semantic_category = 'layout',
                      notes = 'Suppress paragraph indent on the next line',
                      insert_template = '\noindent '
                  WHERE name = 'noindent' AND kind = 'command' AND package_slug IS NULL;

                -- ── Insert new command rows for missing sizes ───────────────
                -- 'huge', 'LARGE', 'Large', 'large', 'small', 'footnotesize',
                -- 'scriptsize', 'tiny' had no command-kind row. Add them with
                -- 'shimmed' coverage + brace-group templates so they surface
                -- in the panel/palette/slash without a redeploy of any
                -- editor code.
                INSERT INTO latex_tokens
                  (id, name, kind, package_slug, coverage_level, expects_body,
                   semantic_category, insert_template, notes, created_at, updated_at)
                VALUES
                  (gen_random_uuid(),'huge','command',NULL,'shimmed',false,'font','{\huge |SELECTION|}','Slightly smaller than Huge',NOW(),NOW()),
                  (gen_random_uuid(),'LARGE','command',NULL,'shimmed',false,'font','{\LARGE |SELECTION|}','Larger than Large, smaller than huge',NOW(),NOW()),
                  (gen_random_uuid(),'Large','command',NULL,'shimmed',false,'font','{\Large |SELECTION|}','Larger than large',NOW(),NOW()),
                  (gen_random_uuid(),'large','command',NULL,'shimmed',false,'font','{\large |SELECTION|}','Slightly larger than normalsize',NOW(),NOW()),
                  (gen_random_uuid(),'small','command',NULL,'shimmed',false,'font','{\small |SELECTION|}','Slightly smaller than normalsize',NOW(),NOW()),
                  (gen_random_uuid(),'footnotesize','command',NULL,'shimmed',false,'font','{\footnotesize |SELECTION|}','Smaller than small — used in footnotes',NOW(),NOW()),
                  (gen_random_uuid(),'scriptsize','command',NULL,'shimmed',false,'font','{\scriptsize |SELECTION|}','Smaller than footnotesize',NOW(),NOW()),
                  (gen_random_uuid(),'tiny','command',NULL,'shimmed',false,'font','{\tiny |SELECTION|}','Smallest standard size',NOW(),NOW())
                ON CONFLICT (name, kind, package_slug) DO UPDATE
                  SET coverage_level = EXCLUDED.coverage_level,
                      semantic_category = EXCLUDED.semantic_category,
                      insert_template = EXCLUDED.insert_template,
                      notes = EXCLUDED.notes,
                      updated_at = NOW();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Restore env rows to 'partial' (their previous coverage_level).
                UPDATE latex_tokens SET coverage_level = 'partial'
                  WHERE kind = 'environment' AND package_slug IS NULL
                    AND name IN ('Huge','huge','LARGE','Large','large',
                                 'normalsize','small','footnotesize',
                                 'scriptsize','tiny','raggedright','raggedleft');

                -- Revert command rows to 'unsupported' + clear the new fields.
                UPDATE latex_tokens
                  SET coverage_level = 'unsupported',
                      semantic_category = NULL,
                      insert_template = NULL
                  WHERE kind = 'command' AND package_slug IS NULL
                    AND name IN ('Huge','normalsize','centering',
                                 'raggedright','raggedleft','noindent');

                -- Drop the newly-inserted command rows.
                DELETE FROM latex_tokens
                  WHERE kind = 'command' AND package_slug IS NULL
                    AND name IN ('huge','LARGE','Large','large','small',
                                 'footnotesize','scriptsize','tiny');
            ");
        }
    }
}
