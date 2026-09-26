using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// An equation whose source is itself an align / gather / multline exports as
/// that environment, not wrapped in another.
///
/// <para>A .tex import stores <c>\begin{align}…\end{align}</c> as the equation's
/// source (so KaTeX renders the alignment). The LaTeX export wrapped it in
/// <c>\begin{equation}</c> — or <c>\[…\]</c> when unnumbered — and pdflatex
/// stopped: "Erroneous nesting of equation structures" / "\begin{gather}
/// allowed only in paragraph mode". Found by the LaTeX feature-coverage e2e
/// (math.align, math.align-starred, math.gather, math.multline).</para>
/// </summary>
public class LatexExportImportedMathEnvironmentTests
{
    private static string Latex(object content) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(
                new Document { Id = Guid.NewGuid(), Title = "Eq", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 },
                [new Block
                {
                    Id = Guid.NewGuid(),
                    Type = BlockTypes.Equation,
                    Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
                }],
                [], new LaTeXExportOptions());

    private static string Body(string tex) => tex[tex.IndexOf(@"\begin{document}", StringComparison.Ordinal)..];

    [Theory]
    [InlineData("align", "a &= b \\\\\n&= c")]
    [InlineData("gather", "x = 1 \\\\\ny = 2")]
    [InlineData("multline", "a + b \\\\\n+ c")]
    public void A_numbered_environment_is_emitted_as_itself(string env, string rows)
    {
        var body = Body(Latex(new { latex = $"\\begin{{{env}}}\n{rows}\n\\end{{{env}}}", equationMode = "display" }));

        body.Should().Contain($"\\begin{{{env}}}\n{rows}\n\\end{{{env}}}");
        body.Should().NotContain(@"\begin{equation}");
        body.Should().NotContain(@"\[");
    }

    [Fact]
    public void An_unnumbered_one_takes_the_star_instead_of_a_bracket_wrapper()
    {
        var body = Body(Latex(new { latex = "\\begin{align}\na &= b\n\\end{align}", equationMode = "display", numbered = false }));

        body.Should().Contain("\\begin{align*}\na &= b\n\\end{align*}");
        body.Should().NotContain(@"\[");
    }

    [Fact]
    public void The_label_goes_inside_the_environment()
    {
        var body = Body(Latex(new { latex = "\\begin{align}\na &= b\n\\end{align}", equationMode = "display", label = "eq:a" }));

        body.Should().Contain("\\begin{align}\\label{eq:a}\na &= b\n\\end{align}");
    }

    [Fact]
    public void Split_still_needs_and_gets_its_equation()
    {
        var body = Body(Latex(new { latex = "\\begin{split}\na &= b\n\\end{split}", equationMode = "display" }));

        body.Should().Contain("\\begin{equation}\n\\begin{split}");
    }
}
