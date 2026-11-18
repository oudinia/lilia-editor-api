using FluentAssertions;
using Lilia.Import.Converters;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's Word import test (18 Nov 2026, #187–#197): a paper-style .docx made by pandoc lost its inline
/// equations' paragraphs, its footnotes, its links and its code lines, and its equations didn't compile. The fixtures
/// are the bot's own minimal files (lilia-launch-bot outbox/2026-11-18-word-import/repro).
/// </summary>
public class WordImportBatch9Tests
{
    private static Task<ImportDocument> Parse(string name) =>
        new DocxParser(new OmmlToLatexConverter())
            .ParseAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "docx-corpus", "launch-bot", name + ".docx"));

    // #187: "Each client $i$ holds a loss $f_i$ …" became two numbered equations and lost its words.
    [Fact]
    public async Task A_paragraph_with_inline_equations_keeps_its_words_and_its_equations_inline()
    {
        var doc = await Parse("r1-inline-math");
        doc.Elements.OfType<ImportEquation>().Should().BeEmpty();
        var para = doc.Elements.OfType<ImportParagraph>().Single();
        para.Text.Should().Be("Each client $i$ holds a loss $f_{i}$ and we minimise the average.");
    }

    // #188: the footnote's mark and text were dropped.
    [Fact]
    public async Task A_footnote_is_kept_at_its_mark()
    {
        var doc = await Parse("r2-footnote");
        var para = doc.Elements.OfType<ImportParagraph>().Single();
        para.Text.Should().Contain(@"footnote.\footnote{The footnote text.}").And.EndWith("More text.");
    }

    // #191: U+2212, U+22C5, U+2225 and thin spaces stopped pdflatex; \mathrm, \mathbb and \min were lost.
    [Fact]
    public async Task Word_equation_symbols_become_latex_commands()
    {
        var doc = await Parse("r5-math-symbols");
        var latex = doc.Elements.OfType<ImportEquation>().Single().LatexContent!;
        latex.Should().NotContainAny("−", "⋅", "∥", " ", " ");
        latex.Should().Contain(@"\cdot").And.Contain(@"\mathrm{top}").And.Contain(@"\min_{t<T}")
            .And.Contain(@"\mathbb{E}").And.Contain(@"\|").And.Contain(@"\frac{1}{k");
        latex.Should().NotContain("underset");
    }

    // #192: a code paragraph's line breaks became spaces.
    [Fact]
    public async Task A_code_block_keeps_its_lines()
    {
        var doc = await Parse("r6-code");
        var code = doc.Elements.OfType<ImportCodeBlock>().Single();
        code.Text.Split('\n').Should().Equal("def f(x):", "    y = x + 1", "    return y");
    }

    // #197: the link's target was lost.
    [Fact]
    public async Task A_link_keeps_its_target()
    {
        var doc = await Parse("r11-link");
        doc.Elements.OfType<ImportParagraph>().Single().Text
            .Should().Be(@"Data from the \href{https://leaf.cmu.edu/}{LEAF benchmark}.");
    }

    [Fact]
    public void Upright_letters_and_spaces_in_omml()
    {
        const string ns = "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";
        var (latex, ok, _) = new OmmlToLatexConverter().Convert(
            $"<m:oMath {ns}><m:r><m:t>x</m:t></m:r><m:r><m:t> </m:t></m:r><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>d</m:t></m:r><m:r><m:t>x</m:t></m:r><m:r><m:rPr><m:sty m:val=\"p\"/></m:rPr><m:t>sin</m:t></m:r></m:oMath>");
        ok.Should().BeTrue();
        latex.Should().Be(@"x\, \mathrm{d}x\sin");
    }

    // #196: a comments part declared twice imported as an empty document and reported success.
    [Fact]
    public async Task A_part_declared_twice_still_imports()
    {
        var doc = await Parse("r10-duplicate-part");
        doc.ParseError.Should().BeNull();
        doc.Elements.OfType<ImportParagraph>().Should().Contain(p => p.Text.Contains("Each client"));
    }
}
