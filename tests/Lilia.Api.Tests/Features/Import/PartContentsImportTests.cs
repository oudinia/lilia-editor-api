using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The document sweep's chunk 7 (W-079, a TeX Stack Exchange answer, 16 Nov 2026): "\part{Designing with \LaTeX}" imported
/// as the unclosed text "\part{Designing with LaTeX" (the logo's pattern ate the closing brace) and the PDF stopped; the
/// second \part was lost; "\setcounter{page}{123}" left "page{123}"; \tableofcontents was stripped.
/// </summary>
public class PartContentsImportTests
{
    private const string Book = """
        \documentclass{book}
        \begin{document}
        \tableofcontents
        \part{Designing with \LaTeX}
        \chapter{Beginning to learn design with \LaTeX}
        \section{A section}
        \newpage\setcounter{page}{123}% just for the example
        \part{Designing with HTML}
        \chapter{Beginning with HTML}
        \end{document}
        """;

    [Fact]
    public async Task Parts_stay_as_raw_LaTeX_and_the_contents_is_kept()
    {
        var doc = await new LatexParser().ParseTextAsync(Book);
        doc.Elements.OfType<ImportTableOfContents>().Should().ContainSingle();
        var parts = doc.Elements.OfType<ImportLatexPassthrough>().Select(p => p.LatexCode).ToList();
        parts.Should().Equal(@"\part{Designing with \LaTeX}", @"\part{Designing with HTML}");
        var text = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        text.Should().NotContain("part{").And.NotContain("page{123}");
        doc.Elements.OfType<ImportHeading>().Select(h => h.Text).Should().Contain("Beginning to learn design with LaTeX");
    }

    [Fact]
    public async Task The_contents_becomes_a_contents_block_and_a_part_an_embed()
    {
        var blocks = LatexImportJobExecutor.MapElements((await new LatexParser().ParseTextAsync(Book)).Elements);
        blocks.Select(b => b.type).Should().StartWith(["tableOfContents", "embed"]);
    }

    [Theory]
    [InlineData(@"Typeset with \LaTeX{} today.", "Typeset with LaTeX today.")]
    [InlineData(@"In \TeX{} and \LaTeX.", "In TeX and LaTeX.")]
    public async Task The_logos_take_an_empty_pair_and_nothing_else(string body, string expected)
    {
        var doc = await new LatexParser().ParseTextAsync($"\\documentclass{{article}}\\begin{{document}}{body}\\end{{document}}");
        doc.Elements.OfType<ImportParagraph>().Single().Text.Should().Be(expected);
    }
}
