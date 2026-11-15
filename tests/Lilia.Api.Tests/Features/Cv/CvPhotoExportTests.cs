using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Cv;

/// <summary>
/// The launch bot's #114 (15 Nov 2026): a CV's photo printed as a "[photo]" frame in the PDF. Its file never reached
/// figures/ (only figures' images were collected), and the photo asked for a different name than the export writes.
/// </summary>
public class CvPhotoExportTests
{
    private const string Url = "https://assets.liliaeditor.com/documents/abc/uploads/Ada%20Portrait.PNG";

    private static Block Block(string type, object content, int order = 0) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = order,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    [Fact]
    public void A_photo_s_image_is_collected_with_the_figures()
    {
        var blocks = new[]
        {
            Block("photo", new { src = Url, size = 64 }),
            Block("figure", new { src = "https://x/fig.png", caption = "A" }, 1),
            Block("photo", new { src = "" }, 2),
        };

        FigureFile.Sources(blocks).Select(s => (s.Block.Type, s.Src)).Should().Equal(("photo", Url), ("figure", "https://x/fig.png"));
    }

    [Fact]
    public void The_photo_asks_for_the_file_the_export_writes()
    {
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance)
            .RenderBlockToLatex(Block("photo", new { src = Url, size = 72, position = "right" }));

        var name = FigureFile.Name(Url);
        latex.Should().Contain($@"\IfFileExists{{figures/{name}}}{{\includegraphics[height=72pt]{{figures/{name}}}}}");
        latex.Should().Contain("flushright");
    }

    [Fact]
    public void Photos_are_not_numbered_as_figures_in_a_submission()
        => FigureFile.SubmissionNames([Block("photo", new { src = Url }), Block("figure", new { src = "https://x/fig.png" }, 1)])
            .Should().Equal(new Dictionary<string, string> { ["fig.png"] = "fig1.png" });
}
