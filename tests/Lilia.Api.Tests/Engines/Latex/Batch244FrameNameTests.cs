using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The PDF half of the launch bot's #244 note (19 Nov 2026): a figure whose image can't be found printed
/// "[Missing figure: image-0.png]", the export's file name, for \includegraphics{lilia-launch-no-such-image}. The check's
/// warning already names the written file; the frame now does too. The file the LaTeX includes is unchanged.
/// </summary>
public class Batch244FrameNameTests
{
    private static Document WithFigure(object content)
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
                Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
        };
        return doc;
    }

    private static IEnumerable<string> PreviewAndExport(Document doc)
    {
        yield return new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        yield return new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());
    }

    [Fact]
    public void A_missing_image_with_no_extension_is_framed_with_the_name_the_source_wrote()
    {
        foreach (var latex in PreviewAndExport(WithFigure(new { src = "lilia-launch-no-such-image", caption = "Gone" })))
        {
            latex.Should().Contain("[Missing figure: lilia-launch-no-such-image]").And.NotContain("[Missing figure: image-0.png]");
            latex.Should().Contain(@"\IfFileExists{figures/image-0.png}", "the file the export writes is unchanged");
        }
    }

    [Fact]
    public void The_written_name_is_escaped_for_LaTeX_in_the_frame()
    {
        foreach (var latex in PreviewAndExport(WithFigure(new { src = "figures/results_final.PNG", caption = "R" })))
        {
            latex.Should().Contain(@"[Missing figure: results\_final.PNG]");
            latex.Should().Contain(@"\IfFileExists{figures/results_final.png}");
        }
    }

    [Fact]
    public void A_missing_subfigure_is_framed_with_the_name_the_source_wrote()
    {
        var doc = WithFigure(new
        {
            src = "", caption = "Two panels",
            subfigures = new[] { new { src = "panel-left", caption = "L", label = "" }, new { src = "panel-right", caption = "R", label = "" } },
        });
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        latex.Should().Contain("[Missing figure: panel-left]").And.Contain("[Missing figure: panel-right]");
    }

    [Fact]
    public void An_embedded_image_is_framed_with_a_label_not_its_base64()
    {
        // Zayd's review of API #652/#654: a data: source's written name would be a run of base64 in the frame.
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        foreach (var latex in PreviewAndExport(WithFigure(new { src = png, caption = "Pasted" })))
        {
            latex.Should().Contain("[Missing figure: an embedded image]");
            latex.Should().NotContain("base64").And.NotContain("iVBORw0KGgo");
        }
    }

    [Fact]
    public void An_image_Lilia_stored_is_framed_with_a_label_not_its_GUID()
    {
        const string upload = "https://files.liliaeditor.com/user_2wh3/documents/035db167-2769-40f8-8b56-83ad20d13cb4/images/9b52e5f8-1c2d-4e5f-8a9b-0c1d2e3f4a5b.png";
        foreach (var latex in PreviewAndExport(WithFigure(new { src = upload, caption = "Uploaded" })))
        {
            latex.Should().Contain("[Missing figure: an uploaded image]");
            latex.Should().Contain(@"\IfFileExists{figures/9b52e5f8-1c2d-4e5f-8a9b-0c1d2e3f4a5b.png}", "the file the export packs is unchanged");
        }
    }

    [Fact]
    public void A_TeX_Live_example_image_still_has_no_frame()
    {
        foreach (var latex in PreviewAndExport(WithFigure(new { src = "example-image-a", caption = "From TeX Live" })))
        {
            latex.Should().Contain(@"\includegraphics[width=").And.Contain("{example-image-a}");
            latex.Should().NotContain("Missing figure");
        }
    }
}
