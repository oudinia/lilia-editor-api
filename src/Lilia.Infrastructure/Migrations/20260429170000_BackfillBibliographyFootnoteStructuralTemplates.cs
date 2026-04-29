using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Round 4 of the catalog template backfill (LILIA-91, 2026-04-29).
    /// Targets the next batch of high-utility kernel commands surfaced by
    /// telemetry-adjacent reasoning: bibliography helpers, footnote
    /// markers, table/column structure, math operators, document
    /// structure, and siunitx units.
    ///
    /// Marker rules (recap):
    ///   |SELECTION| → user's selected text drops here
    ///   |CURSOR|    → caret lands here after insert
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillBibliographyFootnoteStructuralTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Bibliography helpers ──────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\addbibresource{|CURSOR|}'
                  WHERE name = 'addbibresource' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\bibliographystyle{|CURSOR|}'
                  WHERE name = 'bibliographystyle' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\bibitem{|CURSOR|}'
                  WHERE name = 'bibitem' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\nocite{|CURSOR|}'
                  WHERE name = 'nocite' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\fullcite{|CURSOR|}'
                  WHERE name = 'fullcite' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Footnote markers (the two raw helpers — \footnote itself
            //    is its own block type, not a token). ────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\footnotemark'
                  WHERE name = 'footnotemark' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\footnotetext{|CURSOR|}'
                  WHERE name = 'footnotetext' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Table structure (booktabs rules + multicolumn/multirow) ──
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\toprule'
                  WHERE name = 'toprule' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\midrule'
                  WHERE name = 'midrule' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\bottomrule'
                  WHERE name = 'bottomrule' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\multicolumn{|CURSOR|}{c}{|SELECTION|}'
                  WHERE name = 'multicolumn' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\multirow{|CURSOR|}{*}{|SELECTION|}'
                  WHERE name = 'multirow' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Math operators ────────────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\argmax_{|CURSOR|}'
                  WHERE name = 'argmax' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\argmin_{|CURSOR|}'
                  WHERE name = 'argmin' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\DeclareMathOperator{\|CURSOR|}{name}'
                  WHERE name = 'DeclareMathOperator' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Document structure (front/main/back matter env switch) ──
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\frontmatter'
                  WHERE name = 'frontmatter' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\mainmatter'
                  WHERE name = 'mainmatter' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\backmatter'
                  WHERE name = 'backmatter' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\appendix'
                  WHERE name = 'appendix' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Author / affiliation (kernel + common packages) ──────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\affiliation{|CURSOR|}'
                  WHERE name = 'affiliation' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\affil{|CURSOR|}'
                  WHERE name = 'affil' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\IEEEauthorblockN{|CURSOR|}'
                  WHERE name = 'IEEEauthorblockN' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\IEEEauthorblockA{|CURSOR|}'
                  WHERE name = 'IEEEauthorblockA' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── siunitx units (most-used) ─────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\num{|CURSOR|}'
                  WHERE name = 'num' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\numlist{|CURSOR|}'
                  WHERE name = 'numlist' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\ang{|CURSOR|}'
                  WHERE name = 'ang' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\meter'
                  WHERE name = 'meter' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\kilogram'
                  WHERE name = 'kilogram' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Code listings (lstinline + minted) ───────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\lstinline|{|CURSOR|}|'
                  WHERE name = 'lstinline' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\mintinline{|CURSOR|}{code}'
                  WHERE name = 'mintinline' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\inputminted{|CURSOR|}{file}'
                  WHERE name = 'inputminted' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\lstinputlisting{|CURSOR|}'
                  WHERE name = 'lstinputlisting' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── User-definition declarations ─────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\newcommand{\|CURSOR|}{}'
                  WHERE name = 'newcommand' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\newenvironment{|CURSOR|}{}{}'
                  WHERE name = 'newenvironment' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\newcounter{|CURSOR|}'
                  WHERE name = 'newcounter' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\newtheorem{|CURSOR|}{name}'
                  WHERE name = 'newtheorem' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\newtheorem*{|CURSOR|}{name}'
                  WHERE name = 'newtheorem*' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Lengths and counters ─────────────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\linewidth'
                  WHERE name = 'linewidth' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\columnwidth'
                  WHERE name = 'columnwidth' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\arabic{|CURSOR|}'
                  WHERE name = 'arabic' AND kind = 'command' AND insert_template IS NULL;
            ");

            // ── Misc full-coverage commands ──────────────────────────────
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = '\hyperref[|CURSOR|]{|SELECTION|}'
                  WHERE name = 'hyperref' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\include{|CURSOR|}'
                  WHERE name = 'include' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\input{|CURSOR|}'
                  WHERE name = 'input' AND kind = 'command' AND insert_template IS NULL;
                UPDATE latex_tokens SET insert_template = '\documentclass{|CURSOR|}'
                  WHERE name = 'documentclass' AND kind = 'command' AND insert_template IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE latex_tokens SET insert_template = NULL
                  WHERE kind = 'command'
                    AND name IN (
                      'addbibresource','bibliographystyle','bibitem','nocite','fullcite',
                      'footnotemark','footnotetext',
                      'toprule','midrule','bottomrule','multicolumn','multirow',
                      'argmax','argmin','DeclareMathOperator',
                      'frontmatter','mainmatter','backmatter','appendix',
                      'affiliation','affil','IEEEauthorblockN','IEEEauthorblockA',
                      'num','numlist','ang','meter','kilogram',
                      'lstinline','mintinline','inputminted','lstinputlisting',
                      'newcommand','newenvironment','newcounter','newtheorem','newtheorem*',
                      'linewidth','columnwidth','arabic',
                      'hyperref','include','input','documentclass'
                    );
            ");
        }
    }
}
