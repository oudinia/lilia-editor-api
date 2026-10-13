using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>Run 3b: a five-level list (MCP-33) and a raw \COMMENT after \IF (MCP-107) failed the compile.</summary>
public class Run3bEmitterTests
{
    private static Block B(string type, object content, int order) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type, SortOrder = order,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static object Nested(int depth) => depth == 0
        ? new { text = "Leaf" }
        : new { text = $"Level {6 - depth}", children = new[] { Nested(depth - 1) } };

    [Fact]
    public async Task A_five_level_list_and_a_raw_comment_compile()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = new List<Block>
            {
                B("list", new { kind = "numbered", items = new[] { Nested(5) } }, 0),
                B("algorithm", new { caption = "Raw", code = "\\IF{$x > 0$}\n\\COMMENT{positive}\n\\STATE $y \\gets x$\n\\ENDIF" }, 1),
            },
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        tex.Should().Contain(@"\STATE \COMMENT{positive}");
        System.Text.RegularExpressions.Regex.Matches(tex, @"\\begin\{enumerate\}").Count.Should().Be(4);

        var dir = Directory.CreateTempSubdirectory("lilia-3b-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "d.tex"), tex);
            using var p = Process.Start(new ProcessStartInfo("pdflatex", "-interaction=nonstopmode -halt-on-error d.tex") { WorkingDirectory = dir, RedirectStandardOutput = true })!;
            await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var log = await File.ReadAllTextAsync(Path.Combine(dir, "d.log"));
            p.ExitCode.Should().Be(0, log[^Math.Min(1500, log.Length)..]);
        }
        finally { Directory.Delete(dir, true); }
    }
}
