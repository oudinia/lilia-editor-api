using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>MCP-63 (run 3): a table wider than its column overflowed under a clean check.</summary>
public class WideTableTests
{
    [Fact]
    public void Each_table_once_at_its_widest_and_small_overhangs_ignored()
    {
        const string log = "Overfull \\hbox (120.5pt too wide) in paragraph at lines 40--52\n"
            + "Overfull \\hbox (130.0pt too wide) in alignment at lines 44--44\n"
            + "Overfull \\hbox (3.1pt too wide) in alignment at lines 80--85\n"
            + "Overfull \\hbox (40.0pt too wide) in paragraph at lines 90--91\n";
        // Lines 41–50 hold a tabular, 80–85 another; 90–91 is prose (a long URL, say).
        var source = string.Join("\n", Enumerable.Range(1, 100).Select(n => n switch
        {
            41 or 80 => "\\begin{tabular}{ll}",
            50 or 85 => "\\end{tabular}",
            _ => "x",
        }));
        var found = WideTables.Find(log, source);
        found.Should().ContainSingle();
        found[0].Should().Be(new WideTables.Overflow(44, 44, 130.0));
        WideTables.Message(found[0]).Should().Contain("130 pt wider");
    }

    // MCP-130 (retested 14 Nov): a code line with a long name ran 356 pt past a two-column paper's
    // column, under a clean check. Listings are found the same way as tables; prose isn't.
    [Fact]
    public void A_code_line_wider_than_its_column_is_found()
    {
        const string log = "Overfull \\hbox (35.8pt too wide) in paragraph at lines 12--13\n"
            + "Overfull \\hbox (356.0pt too wide) in paragraph at lines 13--14\n"
            + "Overfull \\hbox (40.0pt too wide) in paragraph at lines 30--31\n";
        var source = string.Join("\n", Enumerable.Range(1, 40).Select(n => n switch
        {
            11 => "\\begin{lstlisting}[language=Python]",
            15 => "\\end{lstlisting}",
            _ => "x",
        }));
        var found = WideTables.FindCode(log, source);
        found.Should().ContainSingle().Which.Should().Be(new WideTables.Overflow(13, 14, 356.0));
        WideTables.CodeMessage(found[0]).Should().Contain("356 pt wider");
        WideTables.Find(log, source).Should().BeEmpty("a listing is not a table");
    }

    [Fact]
    public async Task In_two_columns_a_long_code_name_is_reported()
    {
        var code = "veryLongIdentifierWithoutAnySpacesThatCannotBeBrokenByListingsAtAll_and_it_keeps_going_on = 1";
        var (tex, log, _) = await Compile(columns: 2, B("code", new { code, language = "python" }));
        WideTables.FindCode(log, tex).Should().ContainSingle();
        var (tex1, log1, _) = await Compile(columns: 2, B("code", new { code = "x = 1", language = "python" }));
        WideTables.FindCode(log1, tex1).Should().BeEmpty();
    }

    // 2 Nov: a table wider than its column is now scaled down to it (FitToLine), so the seven
    // columns that overflowed here before fit, and the check has nothing to report.
    [Fact]
    public async Task A_seven_column_table_wider_than_the_text_is_scaled_to_it()
    {
        var cells = Enumerable.Range(1, 7).Select(i => $"Measurement {i}").ToArray();
        var (tex, log, _) = await Compile(columns: 1, B("table", new { headers = cells, rows = new[] { cells, cells } }));
        tex.Should().Contain(FitToLine.Open);
        WideTables.Find(log, tex).Should().BeEmpty(string.Join("\n", log.Split('\n').Where(l => l.Contains("Overfull")).Take(5)));
    }

    // The owner's screenshot (2 Nov, "LTX-52 CS paper"): in two columns, a four-column table with a long
    // header ran across the gutter and printed over the listing in the right column.
    [Fact]
    public async Task In_two_columns_a_wide_table_stays_in_its_column()
    {
        var headers = new[] { "Variant", "Time per op.", "Extra space", "Cache misses per find" };
        string[] Row(string v) => new[] { v, "$O(\\alpha(m,n))$", "$n$ bytes $+ |V|/B$", "$O(\\log_B \\log^* n)$" };
        var (_, _, words) = await Compile(columns: 2,
            B("table", new { headers, rows = new[] { Row("Rank + compression"), Row("\\textbf{Splice (ours)}") } }),
            B("paragraph", new { text = string.Join(" ", Enumerable.Repeat("Words that fill the right column.", 120)) }));

        // Every word of the table (its rows) ends left of the page's middle: the gutter.
        var table = words.Where(w => w.Page == 1 && w.Text is "Variant" or "Cache" or "misses" or "find" or "Splice" or "compression").ToList();
        table.Should().NotBeEmpty();
        table.Should().OnlyContain(w => w.XMax < w.PageWidth / 2, "the table must not cross into the right column");
    }

    // A table that fits keeps its size: the box scales only what is wider than the line.
    [Fact]
    public async Task A_narrow_table_is_not_enlarged()
    {
        var (_, _, words) = await Compile(columns: 1, B("table", new { headers = new[] { "A", "B" }, rows = new[] { new[] { "1", "2" } } }));
        var a = words.First(w => w.Text == "A");
        (a.YMax - a.YMin).Should().BeLessThan(12, "10pt text set at its own size, not blown up to the line");
    }

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private sealed record Word(int Page, string Text, double XMin, double XMax, double YMin, double YMax, double PageWidth);

    private static async Task<(string Tex, string Log, List<Word> Words)> Compile(int columns, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Wide", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 10, Columns = columns, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = blocks.ToList(),
        };
        var i = 0;
        foreach (var b in doc.Blocks) { b.SortOrder = i++; b.Document = doc; }
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        var dir = Directory.CreateTempSubdirectory("lilia-wide-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "w.tex"), tex);
            await Run(dir, "pdflatex", "-interaction=nonstopmode w.tex");
            var log = await File.ReadAllTextAsync(Path.Combine(dir, "w.log"));
            await Run(dir, "pdftotext", "-bbox w.pdf w.html");
            var html = await File.ReadAllTextAsync(Path.Combine(dir, "w.html"));
            var words = new List<Word>();
            var page = 0; double width = 0;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(html,
                         @"<page width=""([\d.]+)""|<word xMin=""([\d.]+)"" yMin=""([\d.]+)"" xMax=""([\d.]+)"" yMax=""([\d.]+)"">([^<]*)</word>"))
            {
                double D(int g) => double.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture);
                if (m.Groups[1].Success) { page++; width = D(1); continue; }
                words.Add(new Word(page, m.Groups[6].Value, D(2), D(4), D(3), D(5), width));
            }
            return (tex, log, words);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static async Task Run(string dir, string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true })!;
        await Task.WhenAll(p.StandardOutput.ReadToEndAsync(), p.StandardError.ReadToEndAsync());
        await p.WaitForExitAsync();
    }
}
