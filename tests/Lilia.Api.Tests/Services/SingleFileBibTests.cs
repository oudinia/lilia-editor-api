using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-89 (run 3): the one-file .tex ended with \bibliography{references} and shipped no .bib,
/// so every citation printed [?] in 7 of 8 papers. It now carries the .bib in filecontents*.
/// </summary>
public class SingleFileBibTests
{
    private static Document Doc()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Cited", OwnerId = "u", Language = "en", PaperSize = "a4",
            Blocks = new List<Block>(), BibliographyEntries = new List<BibliographyEntry>(),
        };
        doc.Blocks.Add(new Block
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "paragraph", SortOrder = 0,
            Content = JsonDocument.Parse("""{"text":"As shown in @cite{knuth1984}, it works."}"""),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        });
        doc.BibliographyEntries.Add(new BibliographyEntry
        {
            Id = Guid.NewGuid(), DocumentId = doc.Id, CiteKey = "knuth1984", EntryType = "book",
            Data = JsonDocument.Parse("""{"author":"Donald E. Knuth","title":"The TeXbook","year":"1984","publisher":"Addison-Wesley"}"""),
        });
        return doc;
    }

    private static string Tex(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    [Fact]
    public void The_bib_travels_inside_the_file_before_documentclass()
    {
        var tex = Tex(Doc());
        var at = tex.IndexOf(@"\begin{filecontents*}[overwrite]{references.bib}", StringComparison.Ordinal);
        at.Should().BeGreaterThanOrEqualTo(0, tex[..Math.Min(600, tex.Length)]);
        at.Should().BeLessThan(tex.IndexOf(@"\documentclass", StringComparison.Ordinal));
        tex.Should().Contain("knuth1984").And.Contain(@"\bibliography{references}");
    }

    [Fact]
    public void A_document_without_references_is_unchanged()
    {
        var doc = Doc();
        doc.BibliographyEntries.Clear();
        doc.Blocks.First().Content = JsonDocument.Parse("""{"text":"No citations."}""");
        Tex(doc).Should().NotContain("filecontents");
    }

    [Fact]
    public async Task Pdflatex_then_bibtex_resolves_every_citation()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-bib-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "paper.tex"), Tex(Doc()));
            foreach (var (cmd, args) in new[] { ("pdflatex", "-interaction=nonstopmode paper.tex"), ("bibtex", "paper"), ("pdflatex", "-interaction=nonstopmode paper.tex"), ("pdflatex", "-interaction=nonstopmode paper.tex") })
            {
                using var p = Process.Start(new ProcessStartInfo(cmd, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true })!;
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
            }
            var log = await File.ReadAllTextAsync(Path.Combine(dir, "paper.log"));
            File.Exists(Path.Combine(dir, "paper.pdf")).Should().BeTrue(log[^Math.Min(2000, log.Length)..]);
            log.Should().NotContain("Citation `knuth1984' undefined").And.NotContain("There were undefined citations");
            (await File.ReadAllTextAsync(Path.Combine(dir, "paper.bbl"))).Should().Contain("TeXbook");
        }
        finally { Directory.Delete(dir, true); }
    }
}
