using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

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

    [Fact]
    public async Task A_seven_column_table_wider_than_the_text_is_found_at_its_block()
    {
        Block B(string type, object content) => new()
        {
            Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
            Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        var cells = Enumerable.Range(1, 7).Select(i => $"Measurement {i}").ToArray();
        var table = B("table", new { headers = cells, rows = new[] { cells, cells } });
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Wide", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = new List<Block> { B("paragraph", new { text = "Intro." }), table },
        };
        var i = 0;
        foreach (var b in doc.Blocks) { b.SortOrder = i++; b.Document = doc; }
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        var dir = Directory.CreateTempSubdirectory("lilia-wide-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "w.tex"), tex);
            using (var p = Process.Start(new ProcessStartInfo("pdflatex", "-interaction=nonstopmode w.tex") { WorkingDirectory = dir, RedirectStandardOutput = true })!)
            {
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
            }
            var log = await File.ReadAllTextAsync(Path.Combine(dir, "w.log"));
            var wide = WideTables.Find(log, tex);
            wide.Should().NotBeEmpty(string.Join("\n", log.Split('\n').Where(l => l.Contains("Overfull") || l.StartsWith("!")).Take(10)) + "\n" + tex[tex.IndexOf("\\begin{document}")..]);
            new EmittedLatexAudit(tex).BlockAtLine(wide[0].FirstLine).Should().Be(table.Id);
        }
        finally { Directory.Delete(dir, true); }
    }
}
