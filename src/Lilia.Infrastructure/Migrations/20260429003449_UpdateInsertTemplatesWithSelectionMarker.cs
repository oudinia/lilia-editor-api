using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Refines the seeded insert templates to use the new |SELECTION|
    /// marker where it makes sense — the slot the user's selected text
    /// should fill on click. The editor's helper falls back to |CURSOR|
    /// when no selection slot is present and a selection exists, so this
    /// migration only updates templates where there's a clearer "wrap
    /// the selection" semantic than "drop the caret here."
    /// </summary>
    /// <inheritdoc />
    public partial class UpdateInsertTemplatesWithSelectionMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Commands where the selection naturally fills the *only*
            // argument — caption text, emphasised text, footnote body.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\caption{|SELECTION|}'
                  WHERE name = 'caption'   AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\emph{|SELECTION|}'
                  WHERE name = 'emph'      AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\underline{|SELECTION|}'
                  WHERE name = 'underline' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\footnote{|SELECTION|}'
                  WHERE name = 'footnote'  AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\author{|SELECTION|}'
                  WHERE name = 'author'    AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\date{|SELECTION|}'
                  WHERE name = 'date'      AND kind = 'command' AND package_slug IS NULL;

                -- Two-arg commands: caret at first slot (URL / value),
                -- selection at the human-readable second slot.
                UPDATE latex_tokens SET insert_template = '\href{|CURSOR|}{|SELECTION|}'
                  WHERE name = 'href'      AND kind = 'command';

                UPDATE latex_tokens SET insert_template = '\SI{|CURSOR|}{|SELECTION|}'
                  WHERE name = 'SI'        AND kind = 'command' AND package_slug IS NULL;

                -- Environments — selection becomes the body, |CURSOR| (when
                -- present) is for an option slot like `[title=...]`.
                UPDATE latex_tokens SET insert_template = E'\\begin{center}\n  |SELECTION|\n\\end{center}'
                  WHERE name = 'center'    AND kind = 'environment' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = E'\\begin{tcolorbox}[title=|CURSOR|]\n  |SELECTION|\n\\end{tcolorbox}'
                  WHERE name = 'tcolorbox' AND kind = 'environment' AND package_slug = 'tcolorbox';

                -- acronym stays unchanged — the selection has no obvious
                -- single slot inside \acro{}{}; keep |CURSOR| only.
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to the |CURSOR|-only templates from the prior migration.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\caption{|CURSOR|}'
                  WHERE name = 'caption'   AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\emph{|CURSOR|}'
                  WHERE name = 'emph'      AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\underline{|CURSOR|}'
                  WHERE name = 'underline' AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\footnote{|CURSOR|}'
                  WHERE name = 'footnote'  AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\author{|CURSOR|}'
                  WHERE name = 'author'    AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\date{|CURSOR|}'
                  WHERE name = 'date'      AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = '\href{|CURSOR|}{}'
                  WHERE name = 'href'      AND kind = 'command';
                UPDATE latex_tokens SET insert_template = '\SI{|CURSOR|}{}'
                  WHERE name = 'SI'        AND kind = 'command' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = E'\\begin{center}\n  |CURSOR|\n\\end{center}'
                  WHERE name = 'center'    AND kind = 'environment' AND package_slug IS NULL;
                UPDATE latex_tokens SET insert_template = E'\\begin{tcolorbox}[title=|CURSOR|]\n  Body text.\n\\end{tcolorbox}'
                  WHERE name = 'tcolorbox' AND kind = 'environment' AND package_slug = 'tcolorbox';
            ");
        }
    }
}
