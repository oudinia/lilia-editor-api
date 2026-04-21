using FluentAssertions;
using Lilia.Import.Services;
using Lilia.Import.Models;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Batch A coverage tests — proves that NormaliseCoverageEnvironments
/// rewrites tabularx / rSection to kernel equivalents so the main
/// parser handles them natively.
/// </summary>
public class LatexParserCoverageNormaliseTests
{
    private readonly LatexParser _parser = new();

    [Fact]
    public async Task Tabularx_with_X_columns_renders_as_tabular_block()
    {
        const string src = @"
\documentclass{article}
\usepackage{tabularx}
\begin{document}
\begin{tabularx}{\linewidth}{|X|l|}
\hline
Wide & Fixed \\
\hline
\end{tabularx}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        // Should produce a table block, not a raw passthrough — the env
        // was rewritten to tabular at the top of Parse().
        doc.Elements.Should().Contain(e => e.Type == ImportElementType.Table,
            "tabularx should have been rewritten to tabular and parsed as a table");
    }

    [Fact]
    public async Task RSection_from_resume_class_emits_heading_block()
    {
        const string src = @"
\documentclass{resume}
\begin{document}
\begin{rSection}{Education}
Université de Montréal, 2015--2019.
\end{rSection}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);

        var heading = doc.Elements.OfType<ImportHeading>()
            .FirstOrDefault(h => h.Text.Contains("Education"));
        heading.Should().NotBeNull("rSection should have been rewritten to \\section*");
    }

    [Fact]
    public async Task Tabularx_preserves_column_count_despite_X_degradation()
    {
        const string src = @"
\documentclass{article}
\usepackage{tabularx}
\begin{document}
\begin{tabularx}{\textwidth}{|X|X|X|}
A & B & C \\
1 & 2 & 3 \\
\end{tabularx}
\end{document}";

        var doc = await _parser.ParseTextAsync(src);
        var table = doc.Elements.OfType<ImportTable>().FirstOrDefault();
        table.Should().NotBeNull();
        // Column-count survives because we only swap the column type letter,
        // not the number of entries in the colspec.
        table!.ColumnCount.Should().Be(3);
    }
}
