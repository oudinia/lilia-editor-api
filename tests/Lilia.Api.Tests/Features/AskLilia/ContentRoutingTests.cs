using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.AskLilia;

/// <summary>Run 2, MCP-80: content the Typst path prints wrong goes to LaTeX; plain prose stays on Typst.</summary>
public class ContentRoutingTests
{
    private static (string, JsonElement) B(string type, object content) =>
        (type, JsonDocument.Parse(JsonSerializer.Serialize(content)).RootElement);

    [Theory]
    [InlineData("A __underlined__ word.")]
    [InlineData("A ==highlighted== word.")]
    [InlineData(@"Speed \SI{3.0}{\metre}.")]
    [InlineData(@"Euler \(e^{i\pi}+1=0\).")]
    [InlineData(@"Display \[ x^2 = 4 \].")]
    [InlineData("It costs $5 and the deluxe costs $10.")]
    [InlineData("Let $x$ be real.")]
    [InlineData("See Theorem~(thm:a) and @cite{knuth84}.")]
    public void Content_typst_misprints_goes_to_latex(string text) =>
        ContentRouting.WhyLatex([B("paragraph", new { text })]).Should().NotBeNull();

    [Fact]
    public void Plain_prose_stays_on_typst() =>
        ContentRouting.WhyLatex([B("heading", new { text = "Intro", level = 1 }), B("paragraph", new { text = "Plain **bold** and *italic*, 50% done." }),
            B("figure", new { src = "https://x/a_b__c.png", caption = "A picture" })]).Should().BeNull();

    [Fact]
    public void An_equation_goes_to_latex() =>
        ContentRouting.WhyLatex([B("equation", new { latex = "x^2" })]).Should().Be("equations");

    // 10 Nov 2026: the Typst writer dropped a footnote block, so an automatic PDF or preview lost it.
    [Fact]
    public void A_footnote_goes_to_latex() =>
        ContentRouting.WhyLatex([B("paragraph", new { text = "A claim." }), B("footnote", new { text = "Its source." })]).Should().Be("footnotes");

    [Fact]
    public void The_Typst_writer_keeps_a_footnote_as_a_footnote()
    {
        var source = new TypstExportService().BuildTypstDocument(
            Lilia.Api.Tests.Features.Export.Typst.TypstHarness.Doc(),
            [Lilia.Api.Tests.Features.Export.Typst.TypstHarness.Para("A claim.", 0),
             Lilia.Api.Tests.Features.Export.Typst.TypstHarness.Block("footnote", new { text = "Its *source*." }, 1)], null);

        source.Should().Contain("#footnote[Its").And.Contain("source").And.NotContain("Unsupported block type for Typst export: footnote");
        var compiled = Lilia.Api.Tests.Features.Export.Typst.TypstHarness.Compile(source);
        compiled.Ok.Should().BeTrue(compiled.Error);
    }

    // #66 (11 Nov 2026): the Typst PDF printed an editor algorithm as an empty box and a numbered theorem with no number.
    [Fact]
    public void Algorithms_and_numbered_theorems_go_to_LaTeX_an_unnumbered_theorem_stays()
    {
        ContentRouting.WhyLatex([B("algorithm", new { caption = "Sum", lines = new[] { new { indent = 0, keyword = "for", text = "i", comment = "" } } })])
            .Should().Be("algorithms");
        ContentRouting.WhyLatex([B("theorem", new { text = "Every x is y.", theoremType = "theorem" })]).Should().Be("numbered theorems");
        ContentRouting.WhyLatex([B("theorem", new { text = "Every x is y.", theoremType = "theorem", numbered = true })]).Should().Be("numbered theorems");
        ContentRouting.WhyLatex([B("theorem", new { text = "Every x is y.", theoremType = "remark", numbered = false })]).Should().BeNull();
    }
}
