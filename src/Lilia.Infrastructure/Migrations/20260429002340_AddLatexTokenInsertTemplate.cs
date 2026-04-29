using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Per-token starter snippet for the editor's InsertionsPanel + ⌘K
    /// palette. NULL falls back to the default templates baked into the
    /// editor (commands → \name{|CURSOR|}; environments →
    /// \begin{name}\n  |CURSOR|\n\end{name}). Set per-token to ship a
    /// quality skeleton without an editor redeploy.
    ///
    /// Seeds 11 high-traffic tokens with templates that match how
    /// authors actually write them. Future updates to existing rows or
    /// new rows can land via Lilia Admin (TODO) or follow-on migrations.
    /// </summary>
    /// <inheritdoc />
    public partial class AddLatexTokenInsertTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "insert_template",
                table: "latex_tokens",
                type: "text",
                nullable: true);

            // Seed quality starter templates for high-traffic tokens.
            // |CURSOR| marks where the caret should land post-insert.
            // Using parameterised UPDATE to keep slug/name pairs explicit
            // and avoid shell-quoting hazards inside multi-line snippets.
            migrationBuilder.Sql(@"
                -- ── Commands ────────────────────────────────────────────
                UPDATE latex_tokens SET insert_template = '\caption{|CURSOR|}'
                  WHERE name = 'caption'   AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\emph{|CURSOR|}'
                  WHERE name = 'emph'      AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\underline{|CURSOR|}'
                  WHERE name = 'underline' AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\href{|CURSOR|}{}'
                  WHERE name = 'href'      AND kind = 'command';

                UPDATE latex_tokens SET insert_template = '\author{|CURSOR|}'
                  WHERE name = 'author'    AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\date{|CURSOR|}'
                  WHERE name = 'date'      AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\SI{|CURSOR|}{}'
                  WHERE name = 'SI'        AND kind = 'command' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = '\footnote{|CURSOR|}'
                  WHERE name = 'footnote'  AND kind = 'command' AND package_slug IS NULL;

                -- ── Environments ────────────────────────────────────────
                UPDATE latex_tokens SET insert_template = E'\\begin{center}\n  |CURSOR|\n\\end{center}'
                  WHERE name = 'center'    AND kind = 'environment' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = E'\\begin{acronym}\n  \\acro{|CURSOR|}{Definition}\n\\end{acronym}'
                  WHERE name = 'acronym'   AND kind = 'environment' AND package_slug IS NULL;

                UPDATE latex_tokens SET insert_template = E'\\begin{tcolorbox}[title=|CURSOR|]\n  Body text.\n\\end{tcolorbox}'
                  WHERE name = 'tcolorbox' AND kind = 'environment' AND package_slug = 'tcolorbox';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "insert_template",
                table: "latex_tokens");
        }
    }
}
