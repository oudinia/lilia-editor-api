using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Inline math in imported text stays exactly as written.
///
/// <para>The paragraph normaliser flattens unknown <c>\cmd{arg}</c> to its
/// argument and turns <c>\,</c> into a space — and it ran over inline math
/// too, so <c>$\mathbb{R}$</c> imported as <c>$R$</c> and
/// <c>$\operatorname{rank}(A)$</c> as <c>$rank(A)$</c>: different maths,
/// silently. Found by the LaTeX feature-coverage e2e (math.fonts,
/// math.operatorname).</para>
/// </summary>
public class LatexImportInlineMathTests
{
    private static async Task<string> Paragraph(string body)
    {
        var doc = await new LatexParser().ParseTextAsync(
            "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");
        return doc.Elements.OfType<ImportParagraph>().Single().Text;
    }

    [Theory]
    [InlineData(@"$\mathbb{R}, \mathcal{L}, \mathrm{d}x, \mathbf{v}$")]
    [InlineData(@"$\operatorname{rank}(A) = 2$")]
    [InlineData(@"$\int_0^1 f(x)\,dx$")]
    [InlineData(@"$\text{for all } x \in \mathbb{N}$")]
    public async Task Inline_math_is_kept_verbatim(string math)
    {
        (await Paragraph($"Consider {math} here.")).Should().Be($"Consider {math} here.");
    }

    [Fact]
    public async Task Text_around_the_math_is_still_normalised()
    {
        (await Paragraph(@"A \textbf{bold} claim about $\mathbb{R}$ and \emph{it}."))
            .Should().Be(@"A **bold** claim about $\mathbb{R}$ and *it*.");
    }

    [Fact]
    public async Task An_escaped_dollar_is_a_dollar_not_math()
    {
        (await Paragraph(@"It costs \$10 and \textbf{\$20}."))
            .Should().Be("It costs $10 and **$20**.");
    }
}
