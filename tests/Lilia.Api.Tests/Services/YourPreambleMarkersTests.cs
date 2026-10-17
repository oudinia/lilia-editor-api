using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The editor's LaTeX view tells the author's preamble lines from Lilia's (Preamble tab, DESIGN-14,
/// 17 Oct): the preview's .tex wraps them in two marker comments, and only when there are some.
/// </summary>
public class YourPreambleMarkersTests
{
    private static Document Doc(string? preamble)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Markers", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article", CustomPreamble = preamble,
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        doc.Blocks = new List<Block>
        {
            new() { Id = Guid.NewGuid(), Type = "paragraph", SortOrder = 0, Document = doc, DocumentId = doc.Id,
                Content = JsonDocument.Parse("""{"text":"Hello."}"""), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
        };
        return doc;
    }

    private static string Render(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    [Fact]
    public void The_authors_lines_sit_between_the_markers_before_begin_document()
    {
        var tex = Render(Doc("\\newcommand{\\E}{\\mathbb{E}}\n\\usepackage{csquotes}"));
        var start = tex.IndexOf(RenderService.YourPreambleStart, StringComparison.Ordinal);
        var end = tex.IndexOf(RenderService.YourPreambleEnd, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);
        tex.IndexOf("\\begin{document}", StringComparison.Ordinal).Should().BeGreaterThan(end);
        var inside = tex[start..end];
        inside.Should().Contain("\\newcommand{\\E}{\\mathbb{E}}").And.Contain("\\usepackage{csquotes}");
        tex[..start].Should().NotContain("\\newcommand{\\E}");
    }

    [Fact]
    public void Without_a_preamble_of_their_own_there_are_no_markers()
    {
        Render(Doc(null)).Should().NotContain("lilia:your-preamble");
        Render(Doc("   ")).Should().NotContain("lilia:your-preamble");
    }
}
