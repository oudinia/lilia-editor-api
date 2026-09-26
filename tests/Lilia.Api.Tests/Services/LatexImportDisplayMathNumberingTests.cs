using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// <c>\[…\]</c>, <c>$$…$$</c> and <c>displaymath</c> import unnumbered.
///
/// <para>They imported with the default, numbered — so the export wrote
/// <c>\begin{equation}</c> and the PDF printed an equation number the source
/// never had, shifting every later one. Found by the LaTeX feature-coverage
/// e2e (math.display-bracket, math.display-dollars, math.cases …).</para>
/// </summary>
public class LatexImportDisplayMathNumberingTests
{
    private static async Task<ImportEquation> Equation(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");
        return doc.Elements.OfType<ImportEquation>().Single();
    }

    [Theory]
    [InlineData("\\[\nx = 1\n\\]")]
    [InlineData("$$x = 1$$")]
    [InlineData("\\begin{displaymath}\nx = 1\n\\end{displaymath}")]
    public async Task Unnumbered_display_math_stays_unnumbered(string body)
    {
        var eq = await Equation(body);
        eq.LatexContent.Should().Be("x = 1");
        eq.Numbered.Should().BeFalse();
    }

    [Fact]
    public async Task An_equation_environment_is_still_numbered()
    {
        (await Equation("\\begin{equation}\nx = 1\n\\end{equation}")).Numbered.Should().BeTrue();
    }
}
