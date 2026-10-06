using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Themes;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.TexSafety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A table cell's text is read the same way by the preview's emitter (RenderService) and the
/// export's (LaTeXExportService): the author's <c>\textbf{…}</c> and <c>$…$</c> survive, everything
/// else is escaped. The export used to escape cells wholesale, so a bold cell the preview showed
/// bold printed as the literal text <c>\textbf{Ours}</c> in the PDF (found 6 Oct 2026).
/// </summary>
public class TableCellFormattingTests
{
    private static Block Table(object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "table", SortOrder = 1,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    internal static string Export(params Block[] blocks)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "Tables", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 };
        var all = new List<Block> { new() { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Quartz zebra before the table."}""") } };
        all.AddRange(blocks);
        return new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, all, [], new LaTeXExportOptions());
    }

    private static string Preview(Block block) =>
        new RenderService(null!, new Mock<ILogger<RenderService>>().Object).RenderBlockToLatex(block);

    private static readonly string[] AB = ["A", "B"];

    [Fact]
    public void The_export_keeps_bold_and_maths_in_a_cell_and_escapes_the_rest()
    {
        var tex = Export(Table(new
        {
            headers = AB,
            rows = new[] { new[] { @"\textbf{Ours}", @"$\Delta$ 50% & co" }, new[] { "x_1", @"\textbf{a{b}c}" } },
            columnAlign = new[] { "l", "l" },
        }));
        tex.Should().Contain(@"\textbf{Ours} & $\Delta$ 50\% \& co \\");
        tex.Should().Contain(@"x\_1 & \textbf{a\{b\}c} \\");
        tex.Should().NotContain(@"\textbackslash{}textbf");
    }

    [Fact]
    public void The_export_bolds_a_header_once_and_never_around_maths()
    {
        var tex = Export(Table(new { headers = new[] { @"\textbf{Ours}", "$k$", "Plain" }, rows = new[] { new[] { "a", "b", "c" } } }));
        tex.Should().Contain(@"\liliaTableHead{\textbf{Ours}} & \liliaTableHead{$k$} & \liliaTableHead{\textbf{Plain}} \\");
    }

    [Fact]
    public void The_export_and_the_preview_write_the_same_cells()
    {
        var block = Table(new
        {
            headers = new[] { "Model", @"\textbf{Acc}", "Note" },
            rows = new[]
            {
                new[] { @"\textbf{Ours}", @"\textbf{94.8}", @"$p < 0.05$" },
                new[] { "Base_line", "91.2", "[1] see 50%" },
            },
        });
        var export = Export(block);
        var preview = Preview(block);
        foreach (var line in new[]
        {
            @"\liliaHeadRow \liliaTableHead{\textbf{Model}} & {\liliaTableHead{\textbf{Acc}}} & \liliaTableHead{\textbf{Note}} \\",
            @"\textbf{Ours} & \bfseries 94.8 & $p < 0.05$ \\",
            @"Base\_line & 91.2 & {}[1] see 50\% \\",
        })
        {
            preview.Should().Contain(line);
            export.Should().Contain(line);
        }
    }

    [Fact]
    public void Text_cells_in_an_S_column_stay_braced_and_bold_text_keeps_its_bold()
    {
        var tex = Export(Table(new
        {
            headers = new[] { "Model", "Acc" },
            rows = new[] { new[] { "A", "76.1" }, new[] { "B", @"\textbf{n/a}" }, new[] { "C", @"\textbf{82.6}" } },
            columnAlign = new[] { "l", "decimal" },
        }));
        tex.Should().Contain("S[table-format=2.1, mode=text, reset-text-series=false]");
        tex.Should().Contain(@"B & {\textbf{n/a}} \\").And.Contain(@"C & \bfseries 82.6 \\").And.Contain(@"A & 76.1 \\");
    }

    [Theory]
    [InlineData(@"$\input{notes}$")]
    [InlineData(@"$x \immediate\write18{ls}$")]
    [InlineData(@"$\def\x{1}\x$")]
    [InlineData(@"$a & b$")]
    [InlineData(@"$a \\ b$")]
    [InlineData(@"$a % b$")]
    [InlineData(@"$\end{tabular}\begin{tabular}{l}$")]
    [InlineData(@"$\csname input\endcsname$")]
    [InlineData(@"$a}$")]
    [InlineData(@"\textbf{\input{notes}}")]
    [InlineData(@"\textbf{$\write18{ls}$}")]
    public void A_cell_cannot_write_LaTeX_that_is_not_bold_or_maths(string cell)
    {
        var tex = Export(Table(new { headers = AB, rows = new[] { new[] { "x", cell } } }));
        var row = tex.Split('\n').Single(l => l.StartsWith("x & ", StringComparison.Ordinal));
        foreach (var cmd in new[] { @"\input", @"\write", @"\def", @"\end{tabular}", @"\csname", @"\immediate" })
            row.Should().NotContain(cmd);
        row.Should().NotContain(" a & b").And.NotContain(@"a \\ b").And.NotContain("a % b");
        row.Should().EndWith(@" \\");
        TexSourceGuard.Violation(tex).Should().BeNull();
        TexSourceGuard.Violation(Preview(Table(new { headers = AB, rows = new[] { new[] { "x", cell } } }))).Should().BeNull();
    }

    [Fact]
    public void The_guard_accepts_a_table_full_of_formatting()
    {
        var tex = Export(Table(new
        {
            caption = "Results",
            headers = new[] { "Model", "Acc", "Delta" },
            rows = new[]
            {
                new[] { @"\textbf{Ours}", @"\textbf{94.8}", @"$+2.7$" },
                new[] { @"\textbf{$\alpha$-net}", "91.2", @"$\frac{1}{2}$" },
            },
            columnBest = new[] { null, "higher", null },
        }));
        tex.Should().Contain(@"\textbf{$\alpha$-net}");
        TexSourceGuard.Violation(tex).Should().BeNull();
    }

    // ── LatexText.EscapeCell, the reader both emitters share ─────────────

    [Theory]
    [InlineData(@"\textbf{$x$ wins}", @"\textbf{$x$ wins}")]
    [InlineData(@"\textbf{a{b}c}", @"\textbf{a\{b\}c}")]
    [InlineData(@"$\frac{a}{b}$", @"$\frac{a}{b}$")]
    [InlineData(@"$a \% b$", @"$a \% b$")]
    [InlineData(@"$a & b$", @"\$a \& b\$")]
    [InlineData(@"$\input{x}$", @"\$\textbackslash{}input\{x\}\$")]
    [InlineData(@"$ $", @"\$ \$")]
    public void EscapeCell_reads_bold_and_maths_and_nothing_else(string raw, string expected) =>
        LatexText.EscapeCell(raw).Should().Be(expected);

    [Theory]
    [InlineData(@"\textbf{Ours}", @"\textbf{Ours}")]
    [InlineData("$k$", "$k$")]
    [InlineData("50%", @"\textbf{50\%}")]
    [InlineData("", @"\textbf{}")]
    public void HeaderCell_bolds_once(string raw, string expected) =>
        LatexText.HeaderCell(raw).Should().Be(expected);
}

/// <summary>The export's bold cell compiled for real: it must be set in a bold face.</summary>
[Trait("Category", "TableCompile")]
public class TableCellFormattingCompileTests
{
    private static Block Table(object content) => new()
    {
        Id = Guid.NewGuid(), Type = "table", SortOrder = 1,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    [Fact]
    public async Task A_bold_cell_prints_in_a_bold_face_in_the_exported_pdf()
    {
        // No header row and no caption: the only bold text in the document is the author's cell.
        object Content(string cell) => new { rows = new[] { new[] { "Alpha", cell }, new[] { "Beta", "plain" } } };
        var compile = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);

        var plain = await compile.RenderToPdfAsync(TableCellFormattingTests.Export(Table(Content("Ours"))), "pdflatex", timeout: 180);
        (await DocumentThemeCompileTests.Tool("pdffonts", plain)).Should().NotContain("Bold", "nothing is bold without the author's \\textbf");

        var tex = TableCellFormattingTests.Export(Table(Content(@"\textbf{Ours} $x^2$")));
        tex.Should().Contain(@"Alpha & \textbf{Ours} $x^2$ \\");
        TexSourceGuard.Violation(tex).Should().BeNull();
        var pdf = await compile.RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        var text = await DocumentThemeCompileTests.Tool("pdftotext", pdf, "-");
        text.Should().Contain("Ours").And.NotContain("textbf");
        (await DocumentThemeCompileTests.Tool("pdffonts", pdf)).Should().Contain("Bold", "the cell's \\textbf is a bold face in the PDF");
    }
}
