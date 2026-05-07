using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowedBlocksToLatexDocumentClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // LILIA-121 D1 — sectioning enforcement.
            // Each class advertises which sectioning block keys it supports.
            // BlockTypesController reads this when ?docId=X is supplied so the
            // editor's slash menu / ⌘K palette / insertions panel hide
            // \chapter in articles, \frontmatter outside book/memoir, etc.
            //
            // Non-sectioning blocks (paragraph, list, equation, figure,
            // table, code, blockquote, theorem, abstract, bibliography,
            // tableOfContents, pageBreak) are always allowed and not
            // enumerated here.
            migrationBuilder.AddColumn<string>(
                name: "allowed_blocks",
                table: "latex_document_classes",
                type: "jsonb",
                nullable: true);

            // Idempotent — UPDATE … WHERE slug = … so re-running the
            // migration on a populated DB is a no-op.
            migrationBuilder.Sql(@"
UPDATE latex_document_classes SET allowed_blocks =
  '[""section"",""subsection"",""subsubsection"",""paragraph"",""subparagraph"",""part""]'::jsonb
  WHERE slug IN ('article', 'IEEEtran', 'acmart', 'exam');

UPDATE latex_document_classes SET allowed_blocks =
  '[""chapter"",""section"",""subsection"",""subsubsection"",""paragraph"",""subparagraph"",""part"",""frontmatter"",""mainmatter"",""backmatter"",""appendix""]'::jsonb
  WHERE slug IN ('book', 'memoir');

UPDATE latex_document_classes SET allowed_blocks =
  '[""chapter"",""section"",""subsection"",""subsubsection"",""paragraph"",""subparagraph"",""part"",""appendix""]'::jsonb
  WHERE slug = 'report';

UPDATE latex_document_classes SET allowed_blocks =
  '[""section"",""subsection""]'::jsonb
  WHERE slug IN ('moderncv', 'altacv', 'resume');

UPDATE latex_document_classes SET allowed_blocks =
  '[""chapter"",""section"",""subsection""]'::jsonb
  WHERE slug = 'tufte-book';
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allowed_blocks",
                table: "latex_document_classes");
        }
    }
}
