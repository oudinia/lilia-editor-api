using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's batch 16 retest (19 Nov 2026): \char codes inside \texttt (#245), and the duplicate-\documentclass
/// guard that rewrote a \documentclass shown in a verbatim block (#222's note).
/// </summary>
public class Batch18RetestTests
{
    private static Task<ImportDocument> Parse(string body) => new LatexParser().ParseTextAsync(
        "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");

    [Fact]
    public async Task Char_codes_in_texttt_print_the_characters_they_stand_for()
    {
        var doc = await Parse(@"Invoked with the usual \texttt{{\char'134}begin\,\ldots{\char'134}end} construction or with the short form \texttt{\$\,\ldots\$}.");
        var text = doc.Elements.OfType<ImportParagraph>().Single().Text;
        text.Should().Contain("`\\begin …\\end`").And.Contain("`$ …$`");
        text.Should().NotContain("char");
        LaTeXExportService.FormatInlineContent(text).Should().Contain(@"\texttt{\textbackslash{}begin …\textbackslash{}end}");
    }

    [Theory]
    [InlineData(@"\texttt{\char""5C x}", "`\\x`")]
    [InlineData(@"\texttt{\char92 x}", "`\\x`")]
    [InlineData(@"\texttt{a\_b \{c\}}", "`a_b {c}`")]
    public async Task Each_way_of_writing_a_character_code_reads_the_same(string latex, string code) =>
        (await Parse(latex)).Elements.OfType<ImportParagraph>().Single().Text.Should().Be(code);

    [Fact]
    public async Task A_texttt_with_other_commands_is_read_as_before()
    {
        var text = (await Parse(@"See \texttt{\textbf{bold} code}.")).Elements.OfType<ImportParagraph>().Single().Text;
        text.Should().Contain("**bold**");
    }

    [Fact]
    public void A_documentclass_shown_in_a_verbatim_block_is_left_alone()
    {
        var latex = "\\documentclass{article}\n\\begin{document}\n\\begin{verbatim}\n\\documentclass[sigconf]{acmart}\n\\end{verbatim}\n"
            + "Use \\verb|\\documentclass{x}| here.\n\\documentclass{stray}\n\\end{document}";
        var deduped = RenderService.DedupeDocumentClass(latex);
        deduped.Should().Contain("\\documentclass[sigconf]{acmart}").And.Contain("\\verb|\\documentclass{x}|");
        deduped.Should().NotContain("\\documentclass{stray}").And.Contain("% [lilia] removed duplicate \\documentclass");
    }
}
