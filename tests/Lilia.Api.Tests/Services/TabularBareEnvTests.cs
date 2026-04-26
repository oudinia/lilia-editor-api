using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Regression for SG-117: a bare <c>\begin{tabular}…\end{tabular}</c>
/// (without the surrounding <c>\begin{table}</c> wrapper) should
/// produce a <c>table</c> block, not raw LaTeX text.
/// </summary>
public class TabularBareEnvTests
{
    [Fact]
    public async Task Bare_tabular_produces_table_element()
    {
        const string src = @"\documentclass{article}
\begin{document}
Some intro paragraph.

\begin{tabular}{|l|c|r|}
  \hline
  Left & Centre & Right \\
  \hline
  apple & 42 & 3.14 \\
  banana & 99 & 2.71 \\
  \hline
\end{tabular}

Trailing paragraph.
\end{document}";

        var parser = new LatexParser();
        var doc = await parser.ParseTextAsync(src);

        doc.Should().NotBeNull();
        doc.Elements.Should().Contain(e => e is ImportTable,
            "a bare tabular should produce an ImportTable element. Got: " +
            string.Join(", ", doc.Elements.Select(e => e.GetType().Name)));

        // The raw LaTeX must NOT survive in any paragraph block.
        foreach (var el in doc.Elements)
        {
            if (el is ImportParagraph p)
            {
                p.Text.Should().NotContain(@"\begin{tabular}",
                    $"paragraph text leaked tabular env: {p.Text}");
                p.Text.Should().NotContain(@"\end{tabular}");
                p.Text.Should().NotContain(@"\hline");
            }
        }
    }
}
