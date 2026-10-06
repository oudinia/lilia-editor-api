using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Olivia's 2g, the first three items: decimal alignment (siunitx S columns), units in the
/// header, and the best value in bold. The table owns them, so the preview's emitter
/// (RenderService), the export's (LaTeXExportService) and the Typst export all read them, and
/// the LaTeX is the same under every paper.
/// </summary>
public class TableColumnFormatTests
{
    // ── the number reader ───────────────────────────────────────────────

    [Theory]
    [InlineData("76.1", "", "76", "1", false, false)]
    [InlineData("−1.4", "-", "1", "4", false, false)]
    [InlineData("-0.25", "-", "0", "25", false, false)]
    [InlineData("+2.7", "+", "2", "7", false, false)]
    [InlineData("1,204", "", "1204", "", true, false)]
    [InlineData("50.7%", "", "50", "7", false, true)]
    [InlineData("50.7 %", "", "50", "7", false, true)]
    [InlineData("$+2.7$", "+", "2", "7", false, false)]
    [InlineData(".5", "", "", "5", false, false)]
    [InlineData("12", "", "12", "", false, false)]
    public void Reads_numbers_as_authors_write_them(string raw, string sign, string ints, string frac, bool grouped, bool percent)
    {
        var n = TableColumnFormat.ParseNumber(raw);
        n.Should().NotBeNull(raw);
        n!.Sign.Should().Be(sign);
        n.Int.Should().Be(ints);
        n.Frac.Should().Be(frac);
        n.Grouped.Should().Be(grouped);
        n.Percent.Should().Be(percent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("—")]
    [InlineData("n/a")]
    [InlineData("1e-3")]
    [InlineData("1.2.3")]
    [InlineData("3,14")]
    [InlineData("0.93 \\pm 0.01")]
    [InlineData("25.6M")]
    [InlineData("12 ms")]
    public void Does_not_read_text_as_a_number(string raw) =>
        TableColumnFormat.ParseNumber(raw).Should().BeNull();

    [Fact]
    public void A_bold_number_is_still_a_number()
    {
        var n = TableColumnFormat.ParseNumber(@"\textbf{94.8}");
        n.Should().NotBeNull();
        n!.Bold.Should().BeTrue();
        n.Value.Should().Be(94.8);
    }

    [Theory]
    [InlineData("12 ms", "ms", "12")]
    [InlineData("12ms", "ms", "12")]
    [InlineData("45.2%", "%", "45.2")]
    [InlineData("items", "ms", "items")]
    [InlineData("ms", "ms", "ms")]
    [InlineData("12 s", "ms", "12 s")]
    public void Strips_a_unit_only_from_a_number(string raw, string unit, string expected) =>
        TableColumnFormat.StripUnit(raw, unit).Should().Be(expected);

    [Theory]
    [InlineData("Top-1", "%", "Top-1 (%)")]
    [InlineData("Top-1 (%)", "%", "Top-1 (%)")]
    [InlineData("Latency [ms]", "ms", "Latency [ms]")]
    [InlineData("Top-1", null, "Top-1")]
    public void Puts_the_unit_in_the_header_once(string header, string? unit, string expected) =>
        TableColumnFormat.HeaderWithUnit(header, unit).Should().Be(expected);

    // ── the preview's emitter (RenderService) ───────────────────────────

    private static string Preview(object content) =>
        new RenderService(null!, new Mock<ILogger<RenderService>>().Object).RenderBlockToLatex(new Block
        {
            Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "table", SortOrder = 0,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        });

    private static readonly string[] ModelAcc = ["Model", "Acc"];

    [Fact]
    public void A_column_of_numbers_becomes_an_S_column_by_itself()
    {
        var tex = Preview(new { headers = ModelAcc, rows = new[] { new[] { "A", "76.1" }, new[] { "B", "-82.65" }, new[] { "C", "—" } } });

        tex.Should().Contain(@"\begin{tabular}{lS[table-format=-2.2, mode=text]}");
        tex.Should().Contain(@"\liliaHeadRow \liliaTableHead{\textbf{Model}} & {\liliaTableHead{\textbf{Acc}}} \\");
        tex.Should().Contain(@"A & 76.1 \\").And.Contain(@"B & -82.65 \\");
        tex.Should().Contain(@"C & {—} \\", "a dash is text, and text in an S column is braced");
    }

    [Fact]
    public void The_authors_alignment_wins_over_the_automatic_one()
    {
        var rows = new[] { new[] { "A", "76.1" }, new[] { "B", "82.6" } };
        Preview(new { headers = ModelAcc, rows, columnAlign = new[] { "l", "r" } })
            .Should().Contain(@"\begin{tabular}{lr}").And.NotContain("S[");
        Preview(new { headers = ModelAcc, rows, columnAlign = new[] { "l", "c" } })
            .Should().Contain(@"\begin{tabular}{lc}");
        // "l" is what every untouched column stores, so it is not a choice.
        Preview(new { headers = ModelAcc, rows, columnAlign = new[] { "l", "l" } })
            .Should().Contain(@"\begin{tabular}{lS[table-format=2.1, mode=text]}");
    }

    [Fact]
    public void One_number_or_a_word_among_numbers_keeps_the_column_as_it_was()
    {
        Preview(new { headers = ModelAcc, rows = new[] { new[] { "A", "76.1" } } })
            .Should().Contain(@"\begin{tabular}{ll}");
        Preview(new { headers = ModelAcc, rows = new[] { new[] { "A", "76.1" }, new[] { "B", "n/a" } } })
            .Should().Contain(@"\begin{tabular}{ll}").And.Contain(@"B & n/a \\");
    }

    [Fact]
    public void An_explicit_decimal_column_protects_its_text_cells()
    {
        var tex = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "1.5" }, new[] { "B", "n/a" }, new[] { "C", "" } },
            columnAlign = new[] { "l", "decimal" },
        });
        tex.Should().Contain(@"\begin{tabular}{lS[table-format=1.1, mode=text]}");
        tex.Should().Contain(@"B & {n/a} \\").And.Contain(@"C &  \\");
    }

    [Fact]
    public void A_unit_moves_into_the_header_and_out_of_the_cells()
    {
        var tex = Preview(new
        {
            headers = new[] { "Model", "Top-1" },
            rows = new[] { new[] { "A", "45.2%" }, new[] { "B", "50.1 %" }, new[] { "C", "61" } },
            columnUnit = new[] { null, "%" },
        });
        tex.Should().Contain(@"{\liliaTableHead{\textbf{Top-1 (\%)}}}");
        tex.Should().Contain(@"A & 45.2 \\").And.Contain(@"B & 50.1 \\").And.Contain(@"C & 61 \\");
        tex.Should().Contain("S[table-format=2.1, mode=text]");
    }

    [Fact]
    public void A_unit_in_a_text_column_still_goes_to_the_header()
    {
        var tex = Preview(new
        {
            headers = new[] { "Model", "Latency" },
            rows = new[] { new[] { "A", "12 ms" }, new[] { "B", "about 9 ms" } },
            columnUnit = new[] { null, "ms" },
        });
        tex.Should().Contain(@"\textbf{Latency (ms)}").And.Contain(@"A & 12 \\").And.Contain(@"B & about 9 ms \\");
    }

    [Fact]
    public void Bold_best_higher_bolds_the_largest_in_an_S_column()
    {
        var tex = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "76.1" }, new[] { "B", "82.6" }, new[] { "C", "-3.5" } },
            columnBest = new[] { null, "higher" },
        });
        tex.Should().Contain(@"S[table-format=-2.1, mode=text, reset-text-series=false]");
        tex.Should().Contain(@"B & \bfseries 82.6 \\").And.Contain(@"A & 76.1 \\").And.Contain(@"C & -3.5 \\");
    }

    [Fact]
    public void Bold_best_lower_bolds_the_smallest_negative_included()
    {
        var tex = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "76.1" }, new[] { "B", "82.6" }, new[] { "C", "-3.5" } },
            columnBest = new[] { null, "lower" },
        });
        tex.Should().Contain(@"C & \bfseries -3.5 \\").And.Contain(@"B & 82.6 \\");
    }

    [Fact]
    public void A_tie_bolds_every_best_cell_and_all_equal_bolds_none()
    {
        var tie = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "82.6" }, new[] { "B", "82.60" }, new[] { "C", "70" } },
            columnBest = new[] { null, "higher" },
        });
        tie.Should().Contain(@"A & \bfseries 82.6 \\").And.Contain(@"B & \bfseries 82.60 \\").And.Contain(@"C & 70 \\");

        var flat = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "1.0" }, new[] { "B", "1.0" } },
            columnBest = new[] { null, "higher" },
        });
        flat.Should().NotContain(@"\bfseries").And.NotContain("reset-text-series");
    }

    [Fact]
    public void Bold_best_outside_an_S_column_uses_textbf()
    {
        var tex = Preview(new
        {
            headers = ModelAcc,
            rows = new[] { new[] { "A", "76.1" }, new[] { "B", "82.6" } },
            columnAlign = new[] { "l", "r" },
            columnBest = new[] { null, "higher" },
        });
        tex.Should().Contain(@"\begin{tabular}{lr}").And.Contain(@"B & \textbf{82.6} \\");
    }

    [Fact]
    public void Thousands_commas_and_plus_signs_are_kept()
    {
        var tex = Preview(new
        {
            headers = new[] { "Set", "N", "Delta" },
            rows = new[] { new[] { "A", "1,204", "+2.7" }, new[] { "B", "87", "-0.4" } },
        });
        tex.Should().Contain("S[table-format=4, mode=text, group-digits=integer, group-separator={,}, group-minimum-digits=4]");
        tex.Should().Contain("S[table-format=-1.1, mode=text, retain-explicit-plus]");
        tex.Should().Contain(@"A & 1204 & +2.7 \\");
    }

    [Fact]
    public void A_percent_left_in_the_cells_prints_as_text_after_the_number()
    {
        var tex = Preview(new { headers = ModelAcc, rows = new[] { new[] { "A", "45.2%" }, new[] { "B", "5.1%" } } });
        tex.Should().Contain(@"A & 45.2{\%} \\").And.Contain("S[table-format=2.1, mode=text]");
    }

    [Fact]
    public void Merged_cells_turn_the_automatic_column_off()
    {
        var tex = Preview(new
        {
            headers = ModelAcc,
            rows = new object[] { new object[] { new { content = "Both", colspan = 2 } }, new[] { "A", "1.5" }, new[] { "B", "2.5" } },
        });
        tex.Should().NotContain("S[");
    }

    [Fact]
    public void Object_cells_print_their_content()
    {
        // The tool writes {content}; the preview read only {text} and printed them empty.
        var tex = Preview(new { headers = new object[] { new { content = "Model" } }, rows = new[] { new object[] { new { content = "ResNet" } } } });
        tex.Should().Contain(@"\textbf{Model}").And.Contain(@"ResNet \\");
    }

    [Fact]
    public void A_table_with_no_numbers_and_no_options_is_unchanged()
    {
        var tex = Preview(new { headers = new[] { "A", "B" }, rows = new[] { new[] { "x", "y" }, new[] { "z", "w" } } });
        tex.Should().Contain(@"\begin{tabular}{ll}").And.Contain(@"\liliaHeadRow \liliaTableHead{\textbf{A}} & \liliaTableHead{\textbf{B}} \\")
            .And.Contain(@"x & y \\").And.NotContain("{x}");
    }

    // ── the export's emitter (LaTeXExportService) ───────────────────────

    private static string Export(object content)
    {
        var doc = new Document { Id = Guid.NewGuid(), Title = "T", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11 };
        var block = new Block { Id = Guid.NewGuid(), Type = "table", SortOrder = 0, Content = JsonDocument.Parse(JsonSerializer.Serialize(content)) };
        return new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, [block], [], new LaTeXExportOptions());
    }

    [Fact]
    public void The_export_writes_the_same_column_meaning()
    {
        var tex = Export(new
        {
            headers = new[] { "Model", "Top-1", "Delta" },
            rows = new[] { new[] { "A", "45.2%", "+1.0" }, new[] { "B", "50.1%", "-0.5" }, new[] { "C", "n/a", "0.25" } },
            columnAlign = new[] { "l", "decimal", "r" },
            columnUnit = new[] { null, "%", null },
            columnBest = new[] { null, "higher", "lower" },
        });
        tex.Should().Contain(@"\begin{tabular}{lS[table-format=2.1, mode=text, reset-text-series=false]r}");
        tex.Should().Contain(@"{\liliaTableHead{\textbf{Top-1 (\%)}}}");
        tex.Should().Contain(@"A & 45.2 & +1.0 \\");
        tex.Should().Contain(@"B & \bfseries 50.1 & \textbf{-0.5} \\");
        tex.Should().Contain(@"C & {n/a} & 0.25 \\");
    }

    [Fact]
    public void The_export_decimal_aligns_a_column_of_numbers_by_itself()
    {
        var tex = Export(new { headers = ModelAcc, rows = new[] { new[] { "A", "76.1" }, new[] { "B", "82.6" } } });
        tex.Should().Contain(@"\begin{tabular}{lS[table-format=2.1, mode=text]}").And.Contain(@"{\liliaTableHead{\textbf{Acc}}}");
    }

    // ── Typst ───────────────────────────────────────────────────────────

    private static string Typst(object content) =>
        new TypstExportService().BuildTypstDocument(Doc(), [Block("table", content)], null);

    [Fact]
    public void Typst_right_aligns_a_decimal_column_and_carries_unit_and_best()
    {
        var src = Typst(new
        {
            headers = new[] { "Model", "Top-1" },
            rows = new[] { new[] { "A", "45.2%" }, new[] { "B", "50.1%" } },
            columnUnit = new[] { null, "%" },
            columnBest = new[] { null, "higher" },
        });
        src.Should().Contain("align: (left, right),");
        src.Should().MatchRegex(@"\[#strong\[Top-1 \(\\?%\)\]\]");
        src.Should().Contain("[#strong[50.1]]").And.Contain("[45.2]");

        var (text, error) = CompileToText(src);
        text.Should().NotBeNull(error);
        text.Should().Contain("50.1").And.Contain("Top-1");
    }

    [Fact]
    public void Typst_without_a_decimal_column_is_unchanged()
    {
        var src = Typst(new { headers = new[] { "A", "B" }, rows = new[] { new[] { "x", "y" } } });
        src.Should().NotContain("align:");
    }
}

/// <summary>
/// The Source pane's Apply reads the table back through the LaTeX importer, so what the
/// emitter writes for an S column has to come back as the cells, not as its markup.
/// </summary>
public class TableColumnFormatRoundTripTests
{
    [Fact]
    public async Task An_S_column_reads_back_as_its_numbers()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "table", SortOrder = 0,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                headers = new[] { "Model", "Top-1" },
                rows = new[] { new[] { "A", "45.2%" }, new[] { "B", "50.1%" }, new[] { "C", "n/a" } },
                columnAlign = new[] { "l", "decimal" },
                columnUnit = new[] { null, "%" },
                columnBest = new[] { null, "higher" },
            })),
        };
        var tex = new RenderService(null!, new Mock<ILogger<RenderService>>().Object).RenderBlockToLatex(block);
        var doc = await new Lilia.Import.Services.LatexParser().ParseTextAsync("\\documentclass{article}\n\\begin{document}\n" + tex + "\n\\end{document}");
        var table = Lilia.Api.Services.LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, Content: JsonSerializer.SerializeToElement(b.content)))
            .Single(b => b.type == "table").Content;

        var text = table.GetRawText();
        text.Should().NotContain("liliaTableHead").And.NotContain("bfseries").And.NotContain("{n/a}");
        table.GetProperty("headers").EnumerateArray().Select(TableColumnFormat.CellText)
            // The unit comes back as header text (escaped, as every imported cell's % is); the
            // numbers come back bare, and the bold is derived again, so it is not text.
            .Should().Equal("Model", @"Top-1 (\%)");
        table.GetProperty("rows").EnumerateArray().Select(r => TableColumnFormat.CellText(r[1]))
            .Should().Equal("45.2", "50.1", "n/a");
    }
}
