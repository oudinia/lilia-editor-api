using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Maths;

/// <summary>
/// The document sweep (E-025, 16 Nov 2026): one equation with an unclosed brace (\frac{1}{) stopped the whole LaTeX PDF.
/// It now prints as a red note in its place, and the rest of the document compiles.
/// </summary>
public class BrokenEquationTests
{
    private static string Render(string latex, string mode = "display") =>
        EquationLatex.Render(JsonSerializer.SerializeToElement(new { latex, equationMode = mode, displayMode = mode != "inline", numbered = false }));

    [Theory]
    [InlineData(@"\frac{1}{")]
    [InlineData(@"x^{2")]
    [InlineData(@"a}")]
    public void An_unbalanced_equation_prints_a_note_not_its_source(string latex)
    {
        var out1 = Render(latex);
        out1.Should().Contain(EquationLatex.UnbalancedNote).And.NotContain(latex);
        Render(latex, "inline").Should().Contain(EquationLatex.UnbalancedNote);
    }

    [Theory]
    [InlineData(@"\frac{1}{2}")]
    [InlineData(@"\{x \mid x>0\}")]      // escaped braces are text
    [InlineData(@"\left\{ a \right.")]
    public void A_balanced_equation_is_written_as_before(string latex)
        => Render(latex).Should().Contain(latex).And.NotContain(EquationLatex.UnbalancedNote);

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task The_rest_of_the_document_compiles_around_a_broken_equation()
    {
        var latex = "\\documentclass{article}\n" + LaTeXPreamble.Packages + "\\begin{document}\nBefore the equation.\n\n"
                    + Render(@"\frac{1}{") + "\n\nAfter the equation.\n\\end{document}\n";
        var (pdf, _, problems) = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfTolerantWithProblemsAsync(latex, timeout: 120, engine: "pdflatex");
        pdf.Should().NotBeEmpty();
        problems.Should().BeEmpty();
    }
}
