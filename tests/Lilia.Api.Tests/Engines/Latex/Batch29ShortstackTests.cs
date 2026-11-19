using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #176 (19 Nov 2026): a table row of \shortstack{1.0\\(2$\times$)} cells made the import time out. Each
/// "\\(" opened an unclosed \( in the inline-maths pattern, whose ways to read a backslash doubled with every cell (6 cells
/// 1.2 s, 8 timed out after 124 s), and the import left an empty document.
/// </summary>
public class Batch29ShortstackTests
{
    private static async Task<ImportDocument> ParseWithin(string tex, int seconds)
    {
        var parse = new LatexParser().ParseTextAsync(tex);
        (await Task.WhenAny(parse, Task.Delay(TimeSpan.FromSeconds(seconds)))).Should().BeSameAs(parse, $"the import takes under {seconds} s");
        return await parse;
    }

    [Fact]
    public async Task A_row_of_fifteen_shortstack_cells_imports_at_once()
    {
        var cells = string.Join(" & ", Enumerable.Repeat(@"\shortstack{1.0\\(2$\times$)}", 15));
        var doc = await ParseWithin("\\documentclass{article}\n\\begin{document}\n\\begin{tabular}{l" + new string('c', 15) + "}\nRTF & "
            + cells + " \\\\\nRTF & " + cells + " \\\\\n\\end{tabular}\n\\end{document}\n", 10);
        doc.Elements.Should().NotBeEmpty();
    }

    [Fact]
    public async Task One_shortstack_in_a_multirow_header_imports_at_once()
    {
        var doc = await ParseWithin(@"\documentclass{IEEEtran}
\begin{document}
\begin{table*}
\begin{tabular*}{\textwidth}{lccccccccccc}
\multirow{2}{*}{Method} & \multirow{2}{*}{\shortstack{Params.\\(M)}} & a & b & c & d & e & f & g & h & i & j \\
\rowcolor{gray} x & y & 1 & 2 & 3 & 4 & 5 & 6 & 7 & 8 & 9 & 10 \\
\end{tabular*}
\end{table*}
Text with inline \(x+y\) maths.
\end{document}", 10);
        doc.Elements.Should().NotBeEmpty();
    }
}
