using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Round 3 of the catalog template backfill. Targets reference,
    /// citation, and URL kernel commands — the next-most-clicked set
    /// after sizes and inline formatting (rounds 1 + 2).
    ///
    /// Selection-marker resolution rule (recap from useInsertions.ts):
    ///   |SELECTION| → user's selected text drops here
    ///   |CURSOR|    → caret lands here after insert
    ///
    /// References + citations all use |CURSOR| because the natural arg
    /// is a label/key the user types, not text they have selected.
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillReferenceCitationMathTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── References — caret at the label arg ───────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\ref{|CURSOR|}'
                  WHERE name = 'ref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\eqref{|CURSOR|}'
                  WHERE name = 'eqref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\pageref{|CURSOR|}'
                  WHERE name = 'pageref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\autoref{|CURSOR|}'
                  WHERE name = 'autoref' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\nameref{|CURSOR|}'
                  WHERE name = 'nameref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\Cref{|CURSOR|}'
                  WHERE name = 'Cref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\autopageref{|CURSOR|}'
                  WHERE name = 'autopageref' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\label{|CURSOR|}'
                  WHERE name = 'label' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
            ");

            // ── Citations — caret at the cite key ─────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\cite{|CURSOR|}'
                  WHERE name = 'cite' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\citep{|CURSOR|}'
                  WHERE name = 'citep' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\citet{|CURSOR|}'
                  WHERE name = 'citet' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\citeauthor{|CURSOR|}'
                  WHERE name = 'citeauthor' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\citeyear{|CURSOR|}'
                  WHERE name = 'citeyear' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\autocite{|CURSOR|}'
                  WHERE name = 'autocite' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── URL ───────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\url{|CURSOR|}'
                  WHERE name = 'url' AND kind = 'command' AND insert_template IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = NULL
                  WHERE kind = 'command'
                    AND name IN (
                      'ref','eqref','pageref','autoref','nameref','Cref','autopageref','label',
                      'cite','citep','citet','citeauthor','citeyear','autocite',
                      'url'
                    );
            ");
        }
    }
}
