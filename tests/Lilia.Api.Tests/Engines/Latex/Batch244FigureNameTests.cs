using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's note on #244 (19 Nov 2026): \includegraphics{lilia-launch-no-such-image}, a file that exists nowhere, made
/// check_document say "The image image-0.png can't be found … Upload it again": the export's file name, which turns every name
/// without an image extension into image-0.png. The author was asked for a file they never had, a second such missing image
/// wasn't reported at all (both were "image-0.png"), and the publisher findings didn't mention it. The warning now names the
/// file as the source wrote it, missing images are told apart by their source, and a publisher checklist reports them.
/// </summary>
public class Batch244FigureNameTests
{
    private static Task<bool> Missing(string _) => Task.FromResult(false);

    [Fact]
    public void The_written_name_is_the_file_as_the_source_wrote_it()
    {
        FigureFile.WrittenName("lilia-launch-no-such-image").Should().Be("lilia-launch-no-such-image");
        FigureFile.WrittenName("figures/Plot Final.PNG").Should().Be("Plot Final.PNG");
        FigureFile.WrittenName("https://cdn.example.org/u/1/plot.png?sig=abc#x").Should().Be("plot.png");
        FigureFile.Name("lilia-launch-no-such-image").Should().Be("image-0.png", "the export's file name is unchanged");
    }

    [Fact]
    public async Task An_imported_image_that_exists_nowhere_is_named_as_the_source_wrote_it()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\begin{document}
\begin{figure}
\includegraphics{lilia-launch-no-such-image}
\caption{Gone.}
\end{figure}
\end{document}");
        var figure = B("figure", LatexImportJobExecutor.FigureContent(doc.Elements.OfType<ImportImage>().Single()));

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([figure]), Missing);

        missing.Should().ContainSingle().Which.Should().Be((figure.Id, "lilia-launch-no-such-image"));
    }

    [Fact]
    public async Task Two_missing_images_without_an_extension_are_both_reported()
    {
        var a = B("figure", new { src = "results-a", caption = "A" });
        var b = B("figure", new { src = "results-b", caption = "B" });

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([a, b]), Missing);

        missing.Select(m => m.File).Should().Equal("results-a", "results-b");
    }

    [Fact]
    public async Task The_same_missing_image_used_twice_is_reported_once()
    {
        var a = B("figure", new { src = "logo", caption = "A" });
        var b = B("figure", new { src = "logo", caption = "B" });

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([a, b]), Missing);

        missing.Should().ContainSingle().Which.BlockId.Should().Be(a.Id);
    }

    [Fact]
    public async Task A_TeX_Live_example_image_is_never_reported_missing()
    {
        var teX = B("figure", new { src = "example-image-a", caption = "From TeX Live" });

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([teX]), Missing);

        missing.Should().BeEmpty();
    }

    [Fact]
    public void A_missing_image_is_an_error_in_the_publisher_findings()
    {
        var doc = Doc("IEEEtran");
        var report = PublisherCompliance.Check(doc, doc.Blocks.ToList(), "");
        var blockId = Guid.NewGuid().ToString();

        var withFile = PublisherCompliance.WithMissingFiles(report, [(blockId, "lilia-launch-no-such-image")])!;

        withFile.Status.Should().Be("errors");
        withFile.Findings.Should().ContainSingle(f => f.Code == "figure_file").Which.Should().Match<ComplianceFinding>(f =>
            f.Severity == "error" && f.BlockId == blockId && f.Message.Contains("lilia-launch-no-such-image") && !f.Message.Contains("image-0"));
        withFile.Findings.Should().Contain(report.Findings, "the other findings are kept");
    }

    [Fact]
    public void A_figure_with_no_image_at_all_is_still_figure_missing()
    {
        var doc = Doc("IEEEtran");
        var empty = B("figure", new { src = "", caption = "An empty frame" });
        var blocks = doc.Blocks.Append(empty).ToList();

        var report = PublisherCompliance.WithMissingFiles(PublisherCompliance.Check(doc, blocks, ""), [])!;

        report.Findings.Should().Contain(f => f.Code == "figure_missing" && f.BlockId == empty.Id.ToString());
        report.Findings.Should().NotContain(f => f.Code == "figure_file");
    }

    [Fact]
    public void Without_a_publisher_there_is_still_no_checklist()
    {
        PublisherCompliance.WithMissingFiles(null, [("x", "lilia-launch-no-such-image")]).Should().BeNull();
    }
}
