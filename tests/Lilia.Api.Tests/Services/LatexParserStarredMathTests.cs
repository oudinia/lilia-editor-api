using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A starred maths environment must come back starred.
///
/// <para><b>The catalog's diagnosis was wrong.</b> It said <c>align*</c> was
/// <c>partial</c> because "the parser matches exact env names, not *-suffix".
/// The pattern has always matched the star. The defect was one step later:
/// <c>\*?</c> sat outside the capture group, so the environment came back as
/// <c>align</c>, the re-wrap wrote <c>\begin{align}</c>, and every unnumbered
/// equation was imported as a numbered one. Fixing what the note described would
/// have changed nothing.</para>
///
/// <para><b>The star is carried as numbering, not as text.</b> The renderer
/// already treats <c>numbered</c> as a property of the block and emits the star
/// itself. So the contract tested here is the round trip — starred in, starred
/// out — and not that the source keeps a literal <c>*</c>, which would fight the
/// author's numbering toggle and double-wrap <c>equation*</c>.</para>
/// </summary>
public class LatexParserStarredMathTests
{
    private static async Task<ImportEquation> OnlyEquation(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\usepackage{amsmath}\n\\begin{document}\n" +
            body + "\n\\end{document}");

        return doc.Elements.OfType<ImportEquation>().Should().ContainSingle().Subject;
    }

    /// <summary>Import, build the block the way every import path now does,
    /// and render it back to LaTeX.</summary>
    private static async Task<string> RoundTrip(string body)
    {
        var eq = await OnlyEquation(body);
        var content = JsonSerializer.Serialize(EquationBlockContent.From(eq));
        var block = new Block { Id = Guid.NewGuid(), Type = "equation", Content = JsonDocument.Parse(content) };
        return new RenderService(null!, null!).RenderBlockToLatex(block);
    }

    [Theory]
    [InlineData("align")]
    [InlineData("gather")]
    [InlineData("multline")]
    [InlineData("equation")]
    public async Task A_starred_environment_imports_unnumbered(string env)
    {
        var eq = await OnlyEquation($"\\begin{{{env}*}}\na = b\n\\end{{{env}*}}");

        eq.Numbered.Should().BeFalse();
    }

    [Theory]
    [InlineData("align")]
    [InlineData("gather")]
    [InlineData("multline")]
    [InlineData("equation")]
    public async Task An_unstarred_environment_stays_numbered(string env)
    {
        // The other half: the fix must not unnumber everything.
        var eq = await OnlyEquation($"\\begin{{{env}}}\na = b\n\\end{{{env}}}");

        eq.Numbered.Should().BeTrue();
    }

    [Theory]
    [InlineData("align")]
    [InlineData("gather")]
    [InlineData("multline")]
    public async Task Starred_in_starred_out(string env)
    {
        // The contract that matters to the author: what they wrote is what they
        // get back. Before the fix this came out as \begin{align} — numbered.
        var latex = await RoundTrip($"\\begin{{{env}*}}\na &= b \\\\\nc &= d\n\\end{{{env}*}}");

        latex.Should().Contain($"\\begin{{{env}*}}");
        latex.Should().Contain($"\\end{{{env}*}}");
    }

    [Fact]
    public async Task Equation_star_is_wrapped_once_not_twice()
    {
        // The reason the star is not kept as literal text: equation blocks are
        // wrapped by the renderer, so a literal \begin{equation*} in the source
        // would come out nested inside a second equation environment.
        var latex = await RoundTrip("\\begin{equation*}\nE = mc^2\n\\end{equation*}");

        latex.Should().Contain("\\begin{equation*}");
        System.Text.RegularExpressions.Regex.Matches(latex, @"\\begin\{equation").Count.Should().Be(1);
    }

    [Theory]
    [InlineData("align")]
    [InlineData("gather")]
    public async Task Unstarred_in_unstarred_out(string env)
    {
        var latex = await RoundTrip($"\\begin{{{env}}}\na &= b\n\\end{{{env}}}");

        latex.Should().Contain($"\\begin{{{env}}}");
        latex.Should().NotContain($"{env}*");
    }

    [Fact]
    public async Task The_body_survives_intact()
    {
        var eq = await OnlyEquation("\\begin{align*}\nx &= 1 \\\\\ny &= 2\n\\end{align*}");

        eq.LatexContent.Should().Contain("x &= 1").And.Contain("y &= 2");
    }

    [Fact]
    public async Task A_mismatched_star_is_not_silently_paired()
    {
        // \begin{align*} closed by \end{align} is a LaTeX error. The old pattern
        // allowed \*? on both ends independently and would have accepted it; the
        // backreference now requires the same star on both, as LaTeX does.
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n" +
            "\\begin{align*}\na = b\n\\end{align}\n\\end{document}");

        doc.Elements.OfType<ImportEquation>()
            .Should().NotContain(e => e.LatexContent != null && e.LatexContent.Contains("a = b") && !e.Numbered,
                "a mismatched pair must not be imported as a clean unnumbered equation");
    }

    [Fact]
    public void Numbered_is_written_only_when_false()
    {
        // Absent means numbered — that is how the renderer reads it — so existing
        // blocks keep their meaning and the payload does not grow a field on
        // every equation to state the default.
        EquationBlockContent.From(new ImportEquation { LatexContent = "x", Numbered = true })
            .Should().NotContainKey("numbered");
        EquationBlockContent.From(new ImportEquation { LatexContent = "x", Numbered = false })
            .Should().Contain("numbered", false);
    }
}
