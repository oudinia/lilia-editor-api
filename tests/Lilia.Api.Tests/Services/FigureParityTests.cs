using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-111 (run 3b, LTX-98): the check emitted a bare \includegraphics (fatal when the file is
/// missing) and a raw label; the export wrapped the image in \IfFileExists and prefixed the label.
/// </summary>
public class FigureParityTests
{
    private static Document Doc()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        doc.Blocks = new List<Block>
        {
            new()
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, Type = "figure", SortOrder = 0, Document = doc,
                Content = JsonDocument.Parse("""{"src":"https://cdn.example/plot.png","caption":"A plot","label":"plot"}"""),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
        };
        return doc;
    }

    [Fact]
    public void Check_and_export_print_the_same_figure()
    {
        var doc = Doc();
        var check = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        var export = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());
        foreach (var latex in new[] { check, export })
        {
            latex.Should().Contain(@"\IfFileExists{figures/plot.png}").And.Contain("[Missing figure: plot.png]");
            latex.Should().Contain(@"\label{fig:plot}");
        }
    }
}
