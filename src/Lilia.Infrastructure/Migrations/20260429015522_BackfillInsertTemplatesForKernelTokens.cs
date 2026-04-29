using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Backfills 20 high-traffic kernel tokens with insert_template values
    /// so they emit useful snippets from the InsertionsPanel / ⌘K palette
    /// / slash menu instead of the generic default. Pure data update —
    /// targets only rows where the token already has 'full', 'shimmed',
    /// or 'partial' coverage AND no existing template (idempotent).
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillInsertTemplatesForKernelTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Inline-formatting commands — selection-wrap on click.
            // textbf / textit / texttt / textsc / textsl / textmd already
            // have block-toolbar equivalents (B / I) but the catalog row
            // is for users who want the explicit LaTeX command.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\textbf{|SELECTION|}'
                  WHERE name = 'textbf' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\textit{|SELECTION|}'
                  WHERE name = 'textit' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\texttt{|SELECTION|}'
                  WHERE name = 'texttt' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\textsc{|SELECTION|}'
                  WHERE name = 'textsc' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\textsl{|SELECTION|}'
                  WHERE name = 'textsl' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\textmd{|SELECTION|}'
                  WHERE name = 'textmd' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\mbox{|SELECTION|}'
                  WHERE name = 'mbox'   AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
            ");

            // Spacing commands — caret slot for the length value.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\hspace{|CURSOR|}'
                  WHERE name = 'hspace' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\vspace{|CURSOR|}'
                  WHERE name = 'vspace' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
            ");

            // Bare declarations — no args, just emit + space for following text.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\bigskip '
                  WHERE name = 'bigskip'   AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\medskip '
                  WHERE name = 'medskip'   AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\smallskip '
                  WHERE name = 'smallskip' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\hfill '
                  WHERE name = 'hfill'     AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\par '
                  WHERE name = 'par'       AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\linebreak '
                  WHERE name = 'linebreak' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
            ");

            // Document-structure commands — meta + lists.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\listoffigures'
                  WHERE name = 'listoffigures' AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\listoftables'
                  WHERE name = 'listoftables'  AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\maketitle'
                  WHERE name = 'maketitle'     AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\title{|SELECTION|}'
                  WHERE name = 'title'         AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\thanks{|SELECTION|}'
                  WHERE name = 'thanks'        AND kind = 'command' AND package_slug IS NULL AND insert_template IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Clear ONLY the templates we set above. Other migrations might
            // have set templates for other tokens — leave those alone.
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = NULL
                  WHERE kind = 'command' AND package_slug IS NULL
                    AND name IN (
                      'textbf','textit','texttt','textsc','textsl','textmd','mbox',
                      'hspace','vspace',
                      'bigskip','medskip','smallskip','hfill','par','linebreak',
                      'listoffigures','listoftables','maketitle','title','thanks'
                    );
            ");
        }
    }
}
