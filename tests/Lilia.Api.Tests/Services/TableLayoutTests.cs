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
using Lilia.Api.Tests.Typst;
using static Lilia.Api.Tests.Typst.TypstHarness;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The rest of Olivia's 2g (document themes): group headers, table notes, row groups and long
/// tables. All four are the table's meaning, stored in its content, so the preview's emitter
/// (RenderService), the export's (LaTeXExportService) and the Typst export read them through one
/// TableLayout, and the table's LaTeX is the same under every paper.
/// </summary>
public class TableLayoutTests
{
    internal static Block Table(object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "table", SortOrder = 1,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    internal static Document Doc(int columns = 1) => new()
    {
        Id = Guid.NewGuid(), Title = "Tables", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, Columns = columns,
    };

    internal static string Export(Block block, Document? doc = null) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc ?? Doc(), [block], [], new LaTeXExportOptions());

    private static RenderService Render() => new(null!, new Mock<ILogger<RenderService>>().Object);

    private static string Preview(Block block) => Render().RenderBlockToLatex(block);

    /// <summary>The preview's whole-document LaTeX, as the PDF render builds it.</summary>
    internal static string PreviewDocument(Block block, Document? doc = null)
    {
        doc ??= Doc();
        block.DocumentId = doc.Id;
        doc.Blocks = [new Block { Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "paragraph", SortOrder = 0, Content = JsonDocument.Parse("""{"text":"Quartz zebra before the table."}""") }, block];
        return Render().RenderToLatex(doc);
    }

    internal static object Cell(string content, string? note = null) => note is null ? new { content } : new { content, note };

    internal static object[][] Rows(int n) =>
        Enumerable.Range(1, n).Select(i => new object[] { $"Model{i}", $"{70 + i}.5", $"{80 + i}.25" }).ToArray();

    private static string Both(object content)
    {
        var block = Table(content);
        var export = Export(block);
        var preview = Preview(block);
        return export + "\n%%%%\n" + preview;
    }

    // ── group headers ───────────────────────────────────────────────────

    [Fact]
    public void A_group_header_spans_its_columns_above_the_header_with_a_trimmed_rule()
    {
        var block = Table(new
        {
            headers = new[] { "Model", "Top-1", "Top-5" },
            rows = new[] { new[] { "A", "76.1", "92.9" }, new[] { "B", "82.6", "96.0" } },
            headerGroups = new[] { new { label = "ImageNet-1k", start = 1, span = 2 } },
        });
        foreach (var tex in new[] { Export(block), Preview(block) })
        {
            var lines = tex.Split('\n').Select(l => l.TrimEnd()).ToList();
            var top = lines.IndexOf(@"\toprule");
            lines[top + 1].Should().Be(@"\liliaHeadRow  & \multicolumn{2}{c}{\liliaTableHead{\textbf{ImageNet-1k}}} \\");
            lines[top + 2].Should().Be(@"\cmidrule(lr){2-3}");
            lines[top + 3].Should().StartWith(@"\liliaHeadRow \liliaTableHead{\textbf{Model}}");
            lines[top + 4].Should().Be(@"\midrule");
            TexSourceGuard.Violation(tex).Should().BeNull();
        }
    }

    [Fact]
    public void Groups_that_overlap_run_off_the_table_or_have_no_header_are_not_written()
    {
        var rows = new[] { new[] { "A", "1", "2" } };
        var groups = new object[]
        {
            new { label = "One", start = 0, span = 2 },
            new { label = "Overlaps", start = 1, span = 2 },
            new { label = "Off the end", start = 2, span = 5 },
        };
        var tex = Export(Table(new { headers = new[] { "A", "B", "C" }, rows, headerGroups = groups }));
        tex.Should().Contain(@"\liliaHeadRow \multicolumn{2}{c}{\liliaTableHead{\textbf{One}}} &  \\").And.Contain(@"\cmidrule(lr){1-2}");
        tex.Should().NotContain("Overlaps").And.NotContain("Off the end");

        Export(Table(new { rows, headerGroups = groups })).Should().NotContain("cmidrule").And.NotContain("One");
    }

    [Fact]
    public void A_group_label_is_a_header_cell_its_text_escaped()
    {
        Export(Table(new
        {
            headers = new[] { "A", "B" }, rows = new[] { new[] { "x", "y" } },
            headerGroups = new[] { new { label = @"50% & \textbf{more}", start = 0, span = 2 } },
        })).Should().Contain(@"\multicolumn{2}{c}{\liliaTableHead{\textbf{50\% \& \textbf{more}}}} \\");
    }

    // ── notes ───────────────────────────────────────────────────────────

    [Fact]
    public void Notes_get_marks_in_reading_order_and_print_under_the_table()
    {
        var content = new
        {
            caption = "Results",
            headers = new object[] { Cell("Model", "Reported numbers."), "Top-1", "Top-5" },
            rows = new object[]
            {
                new object[] { Cell("Ours", "Trained with our schedule; 50% longer."), Cell("82.6", "Single crop."), "96.0" },
                new object[] { "Base", "76.1", Cell("92.9", "Single crop.") },
            },
        };
        foreach (var tex in new[] { Export(Table(content)), Preview(Table(content)) })
        {
            tex.Should().Contain(@"\liliaTableHead{\textbf{Model}\tnote{a}}");
            // An S column's number keeps its alignment: the mark is braced after it.
            tex.Should().Contain(@"Ours\tnote{b} & 82.6{\tnote{c}} & 96.0 \\");
            tex.Should().Contain(@"Base & 76.1 & 92.9{\tnote{c}} \\", "one note text written twice shares its mark");
            var begin = tex.IndexOf(@"\begin{threeparttable}", StringComparison.Ordinal);
            var tabular = tex.IndexOf(@"\begin{tabular}", StringComparison.Ordinal);
            var notes = tex.IndexOf(@"\begin{tablenotes}\footnotesize", StringComparison.Ordinal);
            var end = tex.IndexOf(@"\end{threeparttable}", StringComparison.Ordinal);
            begin.Should().BePositive().And.BeLessThan(tabular);
            notes.Should().BeGreaterThan(tex.IndexOf(@"\end{tabular}", StringComparison.Ordinal)).And.BeLessThan(end);
            tex.Should().Contain(@"\item[a] Reported numbers.").And.Contain(@"\item[b] Trained with our schedule; 50\% longer.").And.Contain(@"\item[c] Single crop.");
            tex.Should().NotContain(@"\item[d]");
            end.Should().BeLessThan(tex.IndexOf(@"\end{table}", StringComparison.Ordinal));
            TexSourceGuard.Violation(tex).Should().BeNull();
        }
    }

    [Fact]
    public void A_note_cannot_write_LaTeX_that_a_cell_could_not()
    {
        var tex = Export(Table(new
        {
            headers = new[] { "A", "B" },
            rows = new[] { new object[] { Cell("x", @"see \input{secret} and \textbf{this} $\alpha$"), "y" } },
        }));
        tex.Should().Contain(@"\item[a] see \textbackslash{}input\{secret\} and \textbf{this} $\alpha$");
        TexSourceGuard.Violation(tex).Should().BeNull();
    }

    [Fact]
    public void A_table_without_notes_groups_or_row_groups_is_unchanged()
    {
        var tex = Both(new { headers = new[] { "A", "B" }, rows = new[] { new[] { "x", "y" }, new[] { "z", "w" } } });
        tex.Should().NotContain(@"\begin{threeparttable}").And.NotContain(@"\tnote{").And.NotContain("cmidrule").And.NotContain(@"\begin{longtable}");
        tex.Split('\n').Count(l => l.Trim() == @"\midrule").Should().Be(2, "one under each emitter's header");
    }

    [Fact]
    public void Blank_notes_and_marks_past_z_behave()
    {
        Export(Table(new { headers = new[] { "A" }, rows = new[] { new object[] { Cell("x", "  ") } } }))
            .Should().NotContain(@"\tnote{").And.NotContain(@"\begin{threeparttable}");
        TableLayout.MarkFor(0).Should().Be("a");
        TableLayout.MarkFor(25).Should().Be("z");
        TableLayout.MarkFor(26).Should().Be("aa");
        TableLayout.MarkFor(27).Should().Be("ab");
    }

    // ── row groups ──────────────────────────────────────────────────────

    [Fact]
    public void A_row_group_puts_a_rule_before_its_first_row()
    {
        var content = new
        {
            headers = new[] { "Model", "Acc", "F1" },
            rows = Rows(6),
            rowGroups = new[] { 3, 0, 99, 5 },
        };
        foreach (var tex in new[] { Export(Table(content)), Preview(Table(content)) })
        {
            var lines = tex.Split('\n').Select(l => l.TrimEnd()).ToList();
            lines[lines.FindIndex(l => l.StartsWith("Model4 &", StringComparison.Ordinal)) - 1].Should().Be(@"\midrule");
            lines[lines.FindIndex(l => l.StartsWith("Model6 &", StringComparison.Ordinal)) - 1].Should().Be(@"\midrule");
            lines[lines.FindIndex(l => l.StartsWith("Model1 &", StringComparison.Ordinal)) - 1].Should().Be(@"\midrule", "the header's own rule, once");
            lines[lines.FindIndex(l => l.StartsWith("Model1 &", StringComparison.Ordinal)) - 2].Should().NotBe(@"\midrule");
            lines.Count(l => l == @"\midrule").Should().Be(3, "row 0 and row 99 start nothing");
        }
    }

    // ── long tables ─────────────────────────────────────────────────────

    private static object LongContent(int rows, bool notes = false) => new
    {
        caption = "Every run",
        label = "tab:runs",
        headers = new object[] { notes ? Cell("Model", "Reported numbers.") : "Model", "Acc", "F1" },
        rows = Rows(rows),
    };

    [Fact]
    public void Over_forty_body_rows_is_a_longtable_whose_header_repeats_under_continued()
    {
        foreach (var tex in new[] { PreviewDocument(Table(LongContent(41))), Export(Table(LongContent(41))) })
        {
            tex.Should().Contain(@"\begin{longtable}").And.NotContain(@"\begin{table}[");
            var first = tex.IndexOf(@"\endfirsthead", StringComparison.Ordinal);
            var head = tex.IndexOf(@"\endhead", StringComparison.Ordinal);
            first.Should().BePositive();
            head.Should().BeGreaterThan(first);
            var between = tex[first..head];
            between.Should().Contain(@"\caption[]{(continued)}\\").And.Contain(@"\toprule").And.Contain(@"\liliaTableHead{\textbf{Model}}").And.Contain(@"\midrule");
            tex[..first].Should().Contain(@"\caption{Every run}");
            tex.IndexOf("Model1 &", StringComparison.Ordinal).Should().BeGreaterThan(head);
            TexSourceGuard.Violation(tex).Should().BeNull();
        }
    }

    [Fact]
    public void Forty_rows_is_still_a_float()
    {
        PreviewDocument(Table(LongContent(40))).Should().Contain(@"\begin{table}[").And.NotContain(@"\begin{longtable}");
        Export(Table(LongContent(40))).Should().Contain(@"\begin{table}[").And.NotContain(@"\begin{longtable}");
    }

    [Fact]
    public void A_two_column_document_keeps_a_long_table_a_float()
    {
        // longtable is a hard error in two columns (SupportsLongtable).
        PreviewDocument(Table(LongContent(60)), Doc(columns: 2)).Should().NotContain(@"\begin{longtable}");
        Export(Table(LongContent(60)), Doc(columns: 2)).Should().NotContain(@"\begin{longtable}");
    }

    [Fact]
    public void A_single_long_table_block_renders_as_a_longtable_too()
    {
        Preview(Table(LongContent(41))).Should().Contain(@"\begin{longtable}");
        // Unless its document is loaded and has two columns.
        var block = Table(LongContent(41));
        block.Document = Doc(columns: 2);
        Render().RenderBlockToLatex(block).Should().NotContain(@"\begin{longtable}");
    }

    [Fact]
    public void A_long_table_with_notes_prints_them_in_its_last_foot()
    {
        foreach (var tex in new[] { PreviewDocument(Table(LongContent(45, notes: true))), Export(Table(LongContent(45, notes: true))) })
        {
            var open = tex.IndexOf(@"\begin{ThreePartTable}", StringComparison.Ordinal);
            var notes = tex.IndexOf(@"\begin{TableNotes}\footnotesize", StringComparison.Ordinal);
            var longtable = tex.IndexOf(@"\begin{longtable}", StringComparison.Ordinal);
            open.Should().BePositive();
            notes.Should().BeGreaterThan(open).And.BeLessThan(longtable);
            var head = tex.IndexOf(@"\endhead", StringComparison.Ordinal);
            var foot = tex.IndexOf("\\bottomrule\n\\insertTableNotes\n\\endlastfoot", StringComparison.Ordinal);
            foot.Should().BeGreaterThan(head).And.BeLessThan(tex.IndexOf("Model1 &", StringComparison.Ordinal));
            tex.Should().Contain("\\end{longtable}\n\\end{ThreePartTable}");
            tex.Split('\n').Count(l => l.Trim() == @"\bottomrule").Should().Be(1);
        }
    }

    // ── Typst ───────────────────────────────────────────────────────────

    [Fact]
    public void Typst_carries_groups_notes_and_row_groups()
    {
        var src = new TypstExportService().BuildTypstDocument(TypstHarness.Doc(), [Block("table", new
        {
            caption = "Results",
            label = "tab:res",
            headers = new object[] { Cell("Model", "Reported."), "Top-1", "Top-5" },
            rows = new object[] { new object[] { "A", "76.1", "92.9" }, new object[] { "B", Cell("82.6", "Single crop."), "96.0" }, new object[] { "C", "80.0", "95.0" } },
            headerGroups = new[] { new { label = "ImageNet-1k", start = 1, span = 2 } },
            rowGroups = new[] { 2 },
        }), Para("See @tab:res for the numbers.", 1)], null);
        src.Should().Contain("table.cell(colspan: 2)[#strong[ImageNet-1k]]").And.Contain("table.hline(start: 1, end: 3, stroke: 0.5pt)");
        src.Should().Contain("[#strong[Model]#super[a]]").And.Contain("[82.6#super[b]]");
        src.Should().Contain("  table.hline(stroke: 0.5pt),\n  [C]");
        src.Should().Contain("#super[a] Reported.").And.Contain("#super[b] Single crop.");
        src.Should().Contain(") <tab:res>\n#block", "the label stays on the figure, before the notes");
        src.Should().NotContain(TypstExportService.LabelSlot);

        var (text, error) = CompileToText(src);
        text.Should().NotBeNull(error);
        text.Should().Contain("ImageNet-1k").And.Contain("Single crop.").And.Contain("Reported.");
    }

    [Fact]
    public void Typst_lets_a_long_captioned_table_break_across_pages()
    {
        var src = new TypstExportService().BuildTypstDocument(TypstHarness.Doc(), [Block("table", LongContent(60))], null);
        src.Should().Contain("#show figure: set block(breakable: true)");
        src.Should().NotContain(TypstExportService.LabelSlot);
        var (text, error) = CompileToText(src);
        text.Should().NotBeNull(error);
        text.Should().Contain("Model60");
    }
}

/// <summary>
/// The Source pane's Apply (and any import) reads a table back through the LaTeX importer, so what
/// the emitters write for 2g has to come back as the table: the group row as headerGroups, not as
/// the header; notes on their cells; row groups; and a long table's repeated head not as rows.
/// </summary>
public class TableLayoutRoundTripTests
{
    private static async Task<JsonElement> ReadBack(string tex)
    {
        var doc = await new Lilia.Import.Services.LatexParser().ParseTextAsync("\\documentclass{article}\n\\begin{document}\n" + tex + "\n\\end{document}");
        return LatexImportJobExecutor.MapElements(doc.Elements)
            .Select(b => (b.type, Content: JsonSerializer.SerializeToElement(b.content)))
            .Single(b => b.type == "table").Content;
    }

    private static string[] Texts(JsonElement row) => row.EnumerateArray().Select(TableColumnFormat.CellText).ToArray();

    private static object Content(int rows) => new
    {
        caption = "Every run",
        headers = new object[] { TableLayoutTests.Cell("Model", "Reported numbers."), "Acc", "F1" },
        rows = TableLayoutTests.Rows(rows).Select((r, i) => i == 1 ? new object[] { r[0], TableLayoutTests.Cell((string)r[1], "Single crop."), r[2] } : r).ToArray(),
        headerGroups = new[] { new { label = "Scores", start = 1, span = 2 } },
        rowGroups = new[] { 2 },
    };

    [Theory]
    [InlineData(4)]
    [InlineData(45)]
    public async Task The_preview_LaTeX_reads_back_as_the_same_table(int n)
    {
        var block = TableLayoutTests.Table(Content(n));
        var tex = new RenderService(null!, new Mock<ILogger<RenderService>>().Object).RenderBlockToLatex(block);
        tex.Should().Contain(n > TableLayout.LongTableRows ? @"\begin{longtable}" : @"\begin{tabular}");
        var table = await ReadBack(tex);

        Texts(table.GetProperty("headers")).Should().Equal("Model", "Acc", "F1");
        table.GetProperty("headers")[0].GetProperty("note").GetString().Should().Be("Reported numbers.");
        var rows = table.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().HaveCount(n, "the repeated head of a long table is not a row");
        Texts(rows[0]).Should().Equal("Model1", "71.5", "81.25");
        rows[1][1].GetProperty("note").GetString().Should().Be("Single crop.");
        TableColumnFormat.CellText(rows[1][1]).Should().Be("72.5");
        table.GetProperty("headerGroups")[0].GetProperty("label").GetString().Should().Be("Scores");
        table.GetProperty("headerGroups")[0].GetProperty("start").GetInt32().Should().Be(1);
        table.GetProperty("headerGroups")[0].GetProperty("span").GetInt32().Should().Be(2);
        table.GetProperty("rowGroups").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(2);
        table.GetRawText().Should().NotContain("tnote").And.NotContain("cmidrule").And.NotContain("continued");
    }

    [Fact]
    public async Task A_papers_grouped_table_reads_its_group_row_as_groups_and_its_spaces_as_row_groups()
    {
        // Hand-written, as tables in papers are: no Lilia hooks, \addlinespace between groups.
        var table = await ReadBack("""
            \begin{table}[t]
            \caption{Accuracy}
            \begin{tabular}{lcc}
            \toprule
             & \multicolumn{2}{c}{ImageNet} \\
            \cmidrule(lr){2-3}
            Model & Top-1 & Top-5 \\
            \midrule
            A & 1 & 2 \\
            B & 3 & 4 \\
            \addlinespace
            C & 5 & 6 \\
            \bottomrule
            \end{tabular}
            \end{table}
            """);
        Texts(table.GetProperty("headers")).Should().Equal("Model", "Top-1", "Top-5");
        table.GetProperty("rows").EnumerateArray().Select(r => Texts(r)[0]).Should().Equal("A", "B", "C");
        table.GetProperty("headerGroups")[0].GetProperty("label").GetString().Should().Be("ImageNet");
        table.GetProperty("rowGroups").EnumerateArray().Select(x => x.GetInt32()).Should().Equal(2);
    }

    [Fact]
    public async Task A_table_without_2g_layout_reads_back_without_it()
    {
        var tex = new RenderService(null!, new Mock<ILogger<RenderService>>().Object)
            .RenderBlockToLatex(TableLayoutTests.Table(new { headers = new[] { "A", "B" }, rows = new[] { new[] { "x", "y" }, new[] { "z", "w" } } }));
        var table = await ReadBack(tex);
        table.TryGetProperty("headerGroups", out _).Should().BeFalse();
        table.TryGetProperty("rowGroups", out _).Should().BeFalse();
        table.GetProperty("rows")[0][0].ValueKind.Should().Be(JsonValueKind.String);
    }
}

/// <summary>
/// 2g's group headers, notes, row groups and long tables compiled for real with pdfLaTeX and
/// LuaLaTeX, through the export and the preview's whole-document LaTeX.
/// </summary>
[Trait("Category", "TableCompile")]
public class TableLayoutCompileTests
{
    private static Task<byte[]> Compile(string tex, string engine) =>
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, engine, timeout: 180);

    private static object Grouped() => new
    {
        caption = "Accuracy and cost on ImageNet-1k",
        headers = new object[] { "Model", "Params", "Top-1", "Top-5" },
        rows = new object[]
        {
            new object[] { TableLayoutTests.Cell("ResNet-50", "Trained with our schedule."), "25.6", "76.1", "92.9" },
            new object[] { "ViT-B", "86.6", TableLayoutTests.Cell("81.8", "Single crop at 224 px."), "95.6" },
            new object[] { "Ours", "28.1", "\\textbf{82.6}", "96.0" },
            new object[] { "Ours-L", "88.0", "84.1", "96.9" },
        },
        columnUnit = new[] { null, null, "%", "%" },
        headerGroups = new[] { new { label = "ImageNet-1k", start = 2, span = 2 } },
        rowGroups = new[] { 2 },
    };

    [Theory]
    [InlineData("pdflatex")]
    [InlineData("lualatex")]
    public async Task A_grouped_noted_table_compiles_and_prints_its_notes_under_it(string engine)
    {
        foreach (var tex in new[] { TableLayoutTests.Export(TableLayoutTests.Table(Grouped())), TableLayoutTests.PreviewDocument(TableLayoutTests.Table(Grouped())) })
        {
            TexSourceGuard.Violation(tex).Should().BeNull();
            var text = await DocumentThemeCompileTests.Tool("pdftotext", await Compile(tex, engine), "-");
            foreach (var s in new[] { "ImageNet-1k", "Top-1 (%)", "ResNet-50", "81.8", "Ours-L", "Trained with our schedule.", "Single crop at 224 px." })
                text.Should().Contain(s, $"{engine} prints {s}");
            text.IndexOf("Trained with our schedule.", StringComparison.Ordinal)
                .Should().BeGreaterThan(text.IndexOf("Ours-L", StringComparison.Ordinal), "the notes print under the table");
        }
    }

    [Theory]
    [InlineData("pdflatex")]
    [InlineData("lualatex")]
    public async Task A_long_table_breaks_across_pages_and_repeats_its_header_under_continued(string engine)
    {
        object Content(bool notes) => new
        {
            caption = "Every run",
            headers = new object[] { notes ? TableLayoutTests.Cell("Model", "Reported numbers.") : "Model", "Acc", "F1" },
            rows = TableLayoutTests.Rows(90),
            headerGroups = new[] { new { label = "Scores", start = 1, span = 2 } },
            rowGroups = new[] { 30, 60 },
        };
        foreach (var notes in new[] { false, true })
            foreach (var tex in new[] { TableLayoutTests.Export(TableLayoutTests.Table(Content(notes))), TableLayoutTests.PreviewDocument(TableLayoutTests.Table(Content(notes))) })
            {
                tex.Should().Contain(@"\begin{longtable}");
                var text = await DocumentThemeCompileTests.Tool("pdftotext", await Compile(tex, engine), "-");
                var pages = text.Split('\f').Where(p => p.Trim().Length > 0).ToList();
                pages.Count.Should().BeGreaterThan(1, $"{engine}: 90 rows break across pages");
                foreach (var page in pages)
                    page.Should().Contain("Model").And.Contain("Scores", "the header repeats on every page");
                pages[0].Should().Contain("Every run").And.NotContain("(continued)");
                pages.Skip(1).Should().OnlyContain(p => p.Contains("(continued)"));
                text.Should().Contain("Model90");
                if (notes)
                {
                    text.Split("Reported numbers.").Length.Should().Be(2, "the note prints once, under the last page");
                    pages[^1].Should().Contain("Reported numbers.");
                }
            }
    }

    [Fact]
    public async Task Banded_stripes_restart_after_a_row_group()
    {
        // Eight body rows under Cerulean's Banded style: the stripes count drops when a group
        // starts at the fourth row, because the stripes start again after its rule.
        static string Tex(int[] groups)
        {
            var doc = TableLayoutTests.Doc();
            doc.Look = JsonSerializer.Serialize(new { theme = "cerulean", paper = "theme", pins = new { }, tables = new { style = "banded" } });
            return TableLayoutTests.Export(TableLayoutTests.Table(new
            {
                headers = new[] { "Model", "Acc", "F1" },
                rows = TableLayoutTests.Rows(8),
                rowGroups = groups,
            }), doc);
        }
        const string stripe = "#EBF4F8"; // 8 % of #0A76A4 over white, as xcolor mixes it
        int Stripes(byte[] pdf) => DocumentThemeCompileTests.FillColours(pdf).Count(c => Near(c, stripe));

        var plain = Stripes(await Compile(Tex([]), "pdflatex"));
        var grouped = Stripes(await Compile(Tex([3]), "pdflatex"));
        plain.Should().BePositive();
        grouped.Should().BePositive().And.NotBe(plain, "banding restarts after the group's rule");
    }

    private static bool Near((double R, double G, double B) c, string hex)
    {
        double Ch(int i) => Convert.ToInt32(hex.TrimStart('#').Substring(2 * i, 2), 16) / 255.0;
        return Math.Abs(c.R - Ch(0)) < 0.004 && Math.Abs(c.G - Ch(1)) < 0.004 && Math.Abs(c.B - Ch(2)) < 0.004;
    }
}
