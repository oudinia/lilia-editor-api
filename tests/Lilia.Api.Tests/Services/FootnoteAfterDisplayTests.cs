using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-35 (run 3): a footnote block after an equation or an algorithm stayed free-standing, its mark
/// at the start of an empty line. It now goes into the block before it.
/// </summary>
public class FootnoteAfterDisplayTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void In_a_display_a_mark_then_the_text()
    {
        var r = RenderService.FootnoteInto("\\begin{equation}\\label{eq:a}\nx = 1\n\\end{equation}", "\\footnote{Note.}");
        r.Should().Be("\\begin{equation}\\label{eq:a}\nx = 1\\text{\\footnotemark}\n\\end{equation}\n\\footnotetext{Note.}\n");
    }

    [Fact]
    public void In_a_theorem_before_its_end() =>
        RenderService.FootnoteInto("\\begin{theorem}\nIt holds.\n\\end{theorem}", "\\footnote{Note.}")
            .Should().Be("\\begin{theorem}\nIt holds.\\footnote{Note.}\n\\end{theorem}\n");

    [Fact]
    public void On_a_floats_caption() =>
        RenderService.FootnoteInto("\\begin{algorithm}[H]\n\\caption{Sort {fast}}\n\\label{alg:a}\n\\end{algorithm}", "\\footnote{Note.}")
            .Should().Contain("\\caption{Sort {fast}\\protect\\footnotemark}").And.EndWith("\\footnotetext{Note.}\n");

    [Fact]
    public void Nowhere_to_put_it_leaves_it() =>
        RenderService.FootnoteInto("\\begin{verbatim}\nx\n\\end{verbatim}", "\\footnote{Note.}").Should().BeNull();

    [Fact]
    public async Task A_document_with_footnotes_after_each_kind_compiles_with_every_note()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Notes", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = new List<Block>
            {
                B("paragraph", new { text = "Intro." }),
                B("equation", new { latex = "E = mc^2", equationMode = "display", label = "eq:e" }),
                B("footnote", new { text = "NOTEONE" }),
                B("algorithm", new { caption = "Sort", lines = new[] { new { kind = "statement", text = "swap" } } }),
                B("footnote", new { text = "NOTETWO" }),
                B("theorem", new { theoremType = "theorem", text = "It holds." }),
                B("footnote", new { text = "NOTETHREE" }),
            },
        };
        for (var i = 0; i < doc.Blocks.Count; i++) { doc.Blocks.ElementAt(i).SortOrder = i; doc.Blocks.ElementAt(i).Document = doc; }
        var tex = _render.RenderToLatex(doc);
        tex.Should().NotMatchRegex(@"\n\\footnote\{NOTE");
        var dir = Directory.CreateTempSubdirectory("lilia-fn-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "d.tex"), tex);
            for (var pass = 0; pass < 2; pass++)
            {
                using var p = Process.Start(new ProcessStartInfo("pdflatex", "-interaction=nonstopmode d.tex") { WorkingDirectory = dir, RedirectStandardOutput = true })!;
                await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
            }
            File.Exists(Path.Combine(dir, "d.pdf")).Should().BeTrue(await File.ReadAllTextAsync(Path.Combine(dir, "d.log")));
            using var t = Process.Start(new ProcessStartInfo("pdftotext", "d.pdf -") { WorkingDirectory = dir, RedirectStandardOutput = true })!;
            var text = await t.StandardOutput.ReadToEndAsync();
            text.Should().Contain("NOTEONE").And.Contain("NOTETWO").And.Contain("NOTETHREE");
        }
        finally { Directory.Delete(dir, true); }
    }
}
