using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-109 (run 3b, LTX-34): under xelatex, Latin Modern dropped Cyrillic text from the PDF.</summary>
public class CyrillicFontTests
{
    [Fact]
    public async Task Cyrillic_and_greek_print_under_xelatex()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article", LatexEngine = "xelatex",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        doc.Blocks = new List<Block>
        {
            new()
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "paragraph", SortOrder = 0, Document = doc,
                Content = JsonDocument.Parse("""{"text":"Привет, мир. Καλημέρα κόσμε."}"""),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
        };
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        tex.Should().Contain("cmunrm.otf");

        var dir = Directory.CreateTempSubdirectory("lilia-cyr-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "c.tex"), tex);
            using var p = Process.Start(new ProcessStartInfo("xelatex", "-interaction=nonstopmode -halt-on-error c.tex") { WorkingDirectory = dir, RedirectStandardOutput = true })!;
            await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            var log = await File.ReadAllTextAsync(Path.Combine(dir, "c.log"));
            p.ExitCode.Should().Be(0, log[^Math.Min(1500, log.Length)..]);
            log.Should().NotContain("Missing character");
        }
        finally { Directory.Delete(dir, true); }
    }
}
