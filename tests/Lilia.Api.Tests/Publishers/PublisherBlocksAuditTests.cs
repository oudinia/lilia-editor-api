using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// The helper-command audit (publisher polish, 26 Oct 2026): every block an article can hold, in every
/// venue, compiles as exported and as previewed, and the export carries none of Lilia's own commands, so
/// what a publisher receives is plain LaTeX in its class (requirement FR-3, NFR-3).
/// </summary>
public class PublisherBlocksAuditTests
{
    public static TheoryData<string> Venues() => [.. PublisherProfiles.All.Select(p => p.Id)];

    /// <summary>The full document of <see cref="PublisherClassTests"/>, plus every other block an article can hold.</summary>
    internal static Document EveryBlock(PublisherProfile? profile)
    {
        var doc = Doc(profile?.DocumentClass ?? "article", profile?.ClassOptions,
            "Ada Lovelace\\\\Analytical Engines\\\\UK\\\\ada@example.org \\and Alan Turing\\\\Bletchley Park\\\\UK", new[] { "LaTeX", "publishing" });
        doc.PublisherProfile = profile?.Id;
        var more = new[]
        {
            B("heading", new { text = "Every block", level = 1 }),
            B("heading", new { text = "A subsection", level = 2 }),
            B("paragraph", new { text = "Text with *emphasis*, **bold**, `code`, a link [Lilia](https://liliaeditor.com) and $\\alpha$; see Figure~(fig:a)." }),
            B("figure", new { src = "", caption = "A figure", label = "fig:a", width = 0.5 }),
            B("blockquote", new { text = "The engine weaves.", attribution = "Ada Lovelace" }),
            B("quote", new { text = "A plain quotation." }),
            B("verse", new { text = "Line one,\nline two." }),
            B("aside", new { text = "An aside." }),
            B("callout", new { variant = "note", title = "Note", text = "A callout." }),
            B("callout", new { variant = "warning", text = "A warning without a title." }),
            B("footnote", new { text = "A footnote." }),
            B("list", new { items = new[] { "a", "b" }, ordered = false }),
            B("columnLayout", new { mode = "start", columns = 2 }),
            B("paragraph", new { text = "Left column." }),
            B("columnBreak", new { }),
            B("paragraph", new { text = "Right column." }),
            B("columnLayout", new { mode = "end" }),
            B("divider", new { }),
            B("equation", new { latex = "a^2 + b^2 = c^2", mode = "display" }),
            B("embed", new { engine = "latex", code = "\\textit{Raw LaTeX.}" }),
            B("pageBreak", new { }),
        };
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        var bib = blocks.FindIndex(b => b.Type == "bibliography");
        blocks.InsertRange(bib, more);
        blocks.Insert(1, B("tableOfContents", new { }));
        for (var i = 0; i < blocks.Count; i++) { blocks[i].SortOrder = i; blocks[i].Document = doc; blocks[i].DocumentId = doc.Id; }
        doc.Blocks = blocks;
        return doc;
    }

    [Fact]
    public async Task A_column_layout_compiles_in_an_article_too()
    {
        // The audit's finding: no path loaded multicol for a column layout block.
        var doc = EveryBlock(null);
        var renderer = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        foreach (var tex in new[] { Export(doc), new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc) })
        {
            tex.Should().Contain(@"\usepackage{multicol}");
            var pdf = await renderer.RenderToPdfAsync(tex, "pdflatex", timeout: 180);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        }
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    [Theory]
    [MemberData(nameof(Venues))]
    public async Task Every_block_compiles_in_every_venue_as_plain_LaTeX(string venue)
    {
        var profile = PublisherProfiles.Find(venue)!;
        var doc = EveryBlock(profile);
        var export = Export(doc);
        var preview = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

        Regex.Matches(export, @"\\lilia[A-Za-z]+").Select(m => m.Value).Distinct()
            .Should().BeEmpty($"{venue}: a publisher receives none of Lilia's own commands");

        var renderer = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        foreach (var (path, tex) in new[] { ("export", export), ("preview", preview) })
        {
            var pdf = await renderer.RenderToPdfAsync(tex, "pdflatex", timeout: 180);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", $"{venue} {path} compiles");
        }
    }
}
