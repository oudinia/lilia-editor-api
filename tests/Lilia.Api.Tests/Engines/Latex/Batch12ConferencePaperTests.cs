using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's conference papers (18 Nov 2026): an IEEEtran paper stopped on "\_" in a table caption emitted as
/// "\\_" (#178), and an acmart paper on commented-out \url lines that came back as text (#217).
/// </summary>
public class Batch12ConferencePaperTests(ITestOutputHelper output)
{
    [Fact]
    public void A_url_inside_a_comment_stays_commented()
    {
        const string source = "\\section{Datasets}\nVisible paragraph one.\n\n"
            + "% \\begin{itemize}\n"
            + "%     \\item \\textbf{A.} Dataset A~\\footnote{\\url{https://a.example.com/}} is commented out (case 1).\n"
            + "%     \\item Case 2 is only a url \\url{https://b.example.com/} in a comment.\n"
            + "% \\end{itemize}\n\nVisible paragraph two.\n"
            + "A real link \\url{https://c.example.com/50%off} stays.\n";
        var stripped = LatexParser.StripComments(source);
        stripped.Should().NotContain("a.example.com").And.NotContain("b.example.com").And.NotContain("commented out");
        stripped.Should().Contain(@"\url{https://c.example.com/50%off}").And.Contain("Visible paragraph two.");
    }

    [Fact]
    public async Task An_escaped_underscore_in_a_table_caption_stays_one()
    {
        const string source = """
            \documentclass{article}
            \begin{document}
            \begin{table}[t]
            \centering
            \caption{Results on EARS-WHAM\_v2 and EARS-Reverb\_v2.}
            \begin{tabular}{cc} a & b \\ \end{tabular}
            \end{table}
            \end{document}
            """;
        var doc = await new LatexParser().ParseTextAsync(source);
        var table = doc.Elements.OfType<ImportTable>().Single();
        output.WriteLine("stored caption: " + table.Caption);
        var (type, content) = LatexImportJobExecutor.MapElements(doc.Elements).First(x => x.type == "table");
        var block = new Block { Id = Guid.NewGuid(), Type = type, Content = JsonDocument.Parse(JsonSerializer.Serialize(content)) };
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(block);
        output.WriteLine(latex);
        latex.Should().Contain(@"\caption{Results on EARS-WHAM\_v2 and EARS-Reverb\_v2.}").And.NotContain(@"\\_");
    }

    // #218: \textbf in a table's body cells was dropped, so the best results weren't bold.
    [Fact]
    public async Task Bold_in_table_cells_is_kept()
    {
        const string source = """
            \documentclass{article}
            \begin{document}
            \begin{table}[t]
            \caption{Results.}
            \begin{tabular}{lcc}
            Method & Acc & F1 \\
            Ours & \textbf{82.9} & \textit{0.71} \\
            Base & 80.1 & 0.69 \\
            \end{tabular}
            \end{table}
            \end{document}
            """;
        var doc = await new LatexParser().ParseTextAsync(source);
        var row = doc.Elements.OfType<ImportTable>().Single().Rows.Single(r => r[0].Text == "Ours");
        row[1].Text.Should().Be("**82.9**");
        row[2].Text.Should().Be("*0.71*");
    }
}
