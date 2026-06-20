using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <summary>
    /// Data-only catalog catch-up. Greens the two remaining
    /// CatalogIntegrityTests on a fresh Testcontainers DB without
    /// touching LatexParser.cs — every handler_kind here truthfully
    /// mirrors how the parser already dispatches the token.
    ///
    /// Groups (verified against LatexParser.cs):
    ///   (a) Every PassThroughEnvironments member (LatexParser.cs:254)
    ///       gets an 'environment' row with handler_kind='pass-through'.
    ///       INSERT ... ON CONFLICT DO NOTHING so existing rows (incl.
    ///       the theorem-like 'note' kernel row and the already-seeded
    ///       center/flushleft/flushright pass-through rows) are left
    ///       untouched; a follow-up guarded UPDATE fills handler_kind on
    ///       the ~15 members whose row already existed with a NULL
    ///       handler (cvtable, twenty, subs, paracol, frontmatter,
    ///       mainmatter, backmatter, letter, IEEEkeywords, keywords,
    ///       acronyms, CJK, CJK*, acronym, tcolorbox).
    ///   (b) Font-size shim COMMANDS -> handler_kind='shim'
    ///       (preamble-time canonical rewrite).
    ///   (c) moderncv / twentysecondscv COMMANDS rewritten by
    ///       ExpandCvMacro -> handler_kind='shim'.
    ///   (d) longtable ENVIRONMENT -> handler_kind='passthrough' (note:
    ///       NOT 'pass-through'): it is in no audited HashSet, so it
    ///       hits the unknown-environment catch-all ImportLatexPassthrough.
    ///   (e) divider / switchcolumn / columnbreak / sepspace / hbadness
    ///       COMMANDS are handled NOWHERE in the parser. Honest data-only
    ///       fix: demote coverage_level to 'unsupported' and leave
    ///       handler_kind NULL, removing them from the
    ///       "covered token must declare a handler_kind" contract.
    ///
    /// All SQL is idempotent: INSERTs guarded by ON CONFLICT DO NOTHING
    /// (relies on the NULLS NOT DISTINCT unique index from
    /// 20260424030048); UPDATEs guarded by handler_kind IS NULL (a) or by
    /// the explicit name list (b-e) so re-running is a no-op.
    /// </summary>
    public partial class SeedPassThroughEnvsAndFontShimCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- (a) PassThroughEnvironments members ---------------------------------
-- INSERT only the rows that don't already exist; never clobber an
-- existing row (e.g. theorem-like 'note', or the partial pass-through
-- center/flushleft/flushright already seeded by CatalogCatchup).
INSERT INTO latex_tokens (id, name, kind, package_slug, expects_body, semantic_category, maps_to_block_type, coverage_level, handler_kind, notes)
VALUES
  (gen_random_uuid(), 'spacing', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'singlespace', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'doublespace', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'onehalfspace', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'center', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'flushleft', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'flushright', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'raggedright', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'raggedleft', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'small', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'footnotesize', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'scriptsize', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tiny', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'large', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'Large', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'LARGE', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'huge', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'Huge', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'normalsize', 'environment', NULL, true, 'font', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'minipage', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'multicols', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'paracol', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tcolorbox', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'frontmatter', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'backmatter', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'mainmatter', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'letter', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'IEEEkeywords', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'keywords', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'acronym', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'acronyms', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'CJK', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'CJK*', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'cvtable', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'twenty', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'subs', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'block', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'alertblock', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'exampleblock', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'examples', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tblock', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'columns', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'column', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'frame', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'ex', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'dedicatoria', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'agradecimentos', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'resumo', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'folhaderosto', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'fichacatalografica', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'apendicesenv', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'anexosenv', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'titlepage', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'titlingpage', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'declaration', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'acknowledgements', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'abbreviations', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'constants', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'symbols', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'appendices', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'fullwidth', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'docspec', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'Sbox', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'kaobox', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'kaocounter', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'svgraybox', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'trailer', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'questype', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'important', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'warning', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'note', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tip', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'notation', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'problem', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'vocabulary', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'addmargin', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'poster', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'rSubsection', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'entrylist', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'barchart', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'invoicetable', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'homeworkProblem', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'question', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'mdframed', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'twothirdswidth', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'monthCalendar', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'calendar', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'IEEEbiography', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'IEEEbiographynophoto', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'acknowledgement', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'teorem', 'environment', NULL, true, 'theorem', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'talert', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'texample', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tpatternbox', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'titleframe', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'outlineframe', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'summary', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'window', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'instlist', 'environment', NULL, true, 'list', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tips', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'overview', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'backgroundinformation', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'legaltext', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'programcode', 'environment', NULL, true, 'code', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'body', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'pseudoc', 'environment', NULL, true, 'code', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tanim', 'environment', NULL, true, 'theorem', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'keyword', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'sciabstract', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'tocentry', 'environment', NULL, true, 'metadata', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'scheme', 'environment', NULL, true, 'float', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'suppinfo', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'info', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'warn', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'subquestion', 'environment', NULL, true, 'callout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'file', 'environment', NULL, true, 'code', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'commandline', 'environment', NULL, true, 'code', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'refsection', 'environment', NULL, true, 'structure', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'aenumerate', 'environment', NULL, true, 'list', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'recipemethods', 'environment', NULL, true, 'list', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'recipeingredients', 'environment', NULL, true, 'list', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'invoice', 'environment', NULL, true, 'layout', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.'),
  (gen_random_uuid(), 'rem', 'environment', NULL, true, 'theorem', NULL, 'partial', 'pass-through', 'Pass-through environment: parser drops the begin/end wrapper and re-parses the body as ordinary blocks.')
ON CONFLICT (name, kind, package_slug) DO NOTHING;

-- (a continued) Reconcile PassThrough members whose row already exists
-- but the router can't see (coverage NOT in full/partial/shimmed) or
-- whose handler_kind is still NULL. Two real-DB cases this hits:
--   * ~15 rows seeded earlier with a NULL handler_kind
--     (cvtable, twenty, subs, paracol, frontmatter, mainmatter,
--     backmatter, letter, IEEEkeywords, keywords, acronyms, CJK, CJK*,
--     acronym, tcolorbox).
--   * 9 font/size env rows that 20260429_SeedSizeAndAlignmentInsertions
--     demoted to coverage_level='unsupported' (with handler_kind left as
--     'pass-through') so \begin{Huge} stopped polluting the editor
--     InsertionsPanel — large, huge, normalsize, small, footnotesize,
--     scriptsize, tiny, raggedright, raggedleft. The parser STILL passes
--     \begin{huge}...\end{huge} through (they remain in the
--     PassThroughEnvironments HashSet), so the orphan contract requires
--     a router-visible row. We re-promote to 'partial' to tell the truth
--     about parser dispatch. (The command-kind size rows the editor
--     surfaces are untouched — those are the ones the panel uses.)
-- Guard limits the UPDATE to rows that are NULL-handler or already
-- pass-through, so theorem-like / known-structural rows that happen to
-- share a name (e.g. the theorem-like 'note' kernel row) are never
-- overwritten. Idempotent: re-running lands on the same values.
UPDATE latex_tokens
   SET handler_kind = 'pass-through',
       coverage_level = CASE WHEN coverage_level IN ('full','partial','shimmed') THEN coverage_level ELSE 'partial' END,
       updated_at = NOW()
 WHERE kind = 'environment'
   AND package_slug IS NULL
   AND (handler_kind IS NULL OR handler_kind = 'pass-through')
   AND name IN (
     'spacing','singlespace','doublespace','onehalfspace',
     'center','flushleft','flushright','raggedright','raggedleft',
     'small','footnotesize','scriptsize','tiny',
     'large','Large','LARGE','huge','Huge','normalsize',
     'minipage','multicols','paracol','tcolorbox',
     'frontmatter','backmatter','mainmatter','letter',
     'IEEEkeywords','keywords','acronym','acronyms','CJK','CJK*',
     'cvtable','twenty','subs',
     'block','alertblock','exampleblock','examples','tblock',
     'columns','column','frame','ex',
     'dedicatoria','agradecimentos','resumo','folhaderosto',
     'fichacatalografica','apendicesenv','anexosenv',
     'titlepage','titlingpage','declaration','acknowledgements',
     'abbreviations','constants','symbols','appendices',
     'fullwidth','docspec','Sbox',
     'kaobox','kaocounter','svgraybox','trailer','questype',
     'important','warning','note','tip',
     'notation','problem','vocabulary','addmargin','poster',
     'rSubsection','entrylist','barchart','invoicetable',
     'homeworkProblem','question','mdframed','twothirdswidth',
     'monthCalendar','calendar','IEEEbiography','IEEEbiographynophoto',
     'acknowledgement','teorem','talert','texample','tpatternbox',
     'titleframe','outlineframe','summary','window','instlist',
     'tips','overview','backgroundinformation','legaltext','programcode',
     'body','pseudoc','tanim','keyword','sciabstract','tocentry',
     'scheme','suppinfo','info','warn','subquestion',
     'file','commandline','refsection','aenumerate',
     'recipemethods','recipeingredients','invoice','rem'
   );

-- (a continued) Same reconciliation for PACKAGE-scoped pass-through env
-- rows. A handful of PassThroughEnvironments members are catalogued
-- under their owning package (moderncv.cvtable, twentysecondscv.twenty,
-- paracol.paracol, IEEEtran.IEEEkeywords, CJK.CJK / CJK*,
-- tcolorbox.tcolorbox, ...) with coverage='full' but a NULL
-- handler_kind. The parser matches environment names case-insensitively
-- regardless of package, so these dispatch as pass-through exactly like
-- their kernel siblings. Fill the contract column without touching the
-- (correct) coverage_level. Guard limits to NULL / pass-through rows so
-- package-scoped theorem-like / known rows are never overwritten.
UPDATE latex_tokens
   SET handler_kind = 'pass-through', updated_at = NOW()
 WHERE kind = 'environment'
   AND package_slug IS NOT NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND (handler_kind IS NULL OR handler_kind = 'pass-through')
   AND name IN (
     'spacing','singlespace','doublespace','onehalfspace',
     'center','flushleft','flushright','raggedright','raggedleft',
     'small','footnotesize','scriptsize','tiny',
     'large','Large','LARGE','huge','Huge','normalsize',
     'minipage','multicols','paracol','tcolorbox',
     'frontmatter','backmatter','mainmatter','letter',
     'IEEEkeywords','keywords','acronym','acronyms','CJK','CJK*',
     'cvtable','twenty','subs',
     'block','alertblock','exampleblock','examples','tblock',
     'columns','column','frame','ex',
     'dedicatoria','agradecimentos','resumo','folhaderosto',
     'fichacatalografica','apendicesenv','anexosenv',
     'titlepage','titlingpage','declaration','acknowledgements',
     'abbreviations','constants','symbols','appendices',
     'fullwidth','docspec','Sbox',
     'kaobox','kaocounter','svgraybox','trailer','questype',
     'important','warning','note','tip',
     'notation','problem','vocabulary','addmargin','poster',
     'rSubsection','entrylist','barchart','invoicetable',
     'homeworkProblem','question','mdframed','twothirdswidth',
     'monthCalendar','calendar','IEEEbiography','IEEEbiographynophoto',
     'acknowledgement','teorem','talert','texample','tpatternbox',
     'titleframe','outlineframe','summary','window','instlist',
     'tips','overview','backgroundinformation','legaltext','programcode',
     'body','pseudoc','tanim','keyword','sciabstract','tocentry',
     'scheme','suppinfo','info','warn','subquestion',
     'file','commandline','refsection','aenumerate',
     'recipemethods','recipeingredients','invoice','rem'
   );

-- (b) Font-size shim COMMANDS -> shim ---------------------------------
-- (distinct rows from the (a) environments of the same names).
UPDATE latex_tokens
   SET handler_kind = 'shim', updated_at = NOW()
 WHERE kind = 'command'
   AND handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND name IN ('huge','LARGE','Large','large','small',
                'footnotesize','scriptsize','tiny');

-- (c) moderncv / twentysecondscv COMMANDS (ExpandCvMacro) -> shim -----
UPDATE latex_tokens
   SET handler_kind = 'shim', updated_at = NOW()
 WHERE kind = 'command'
   AND handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND name IN ('cvevent','cvdegree','cvitemshort','cvitemwithcomment',
                'cvpubitem','cvuniversity','cvlistitem','twentyitem');

-- (d) longtable ENVIRONMENT -> passthrough (unknown-env catch-all) ----
UPDATE latex_tokens
   SET handler_kind = 'passthrough', updated_at = NOW()
 WHERE kind = 'environment'
   AND handler_kind IS NULL
   AND coverage_level IN ('full','partial','shimmed')
   AND name = 'longtable';

-- (e) Truly-unhandled COMMANDS -> demote to 'unsupported', no handler -
UPDATE latex_tokens
   SET coverage_level = 'unsupported', handler_kind = NULL, updated_at = NOW()
 WHERE kind = 'command'
   AND name IN ('divider','switchcolumn','columnbreak','sepspace','hbadness');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op. These rows describe real parser behavior; reversing
            // would re-introduce catalog orphans / coverage lies.
        }
    }
}
