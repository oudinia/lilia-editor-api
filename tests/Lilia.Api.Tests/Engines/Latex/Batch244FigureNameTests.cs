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

    // Zayd's review of API #652: a data: source's last segment is base64, and a Lilia upload's file name is a GUID.

    private const string Png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private const string Upload = "https://files.liliaeditor.com/user_2wh3/documents/035db167-2769-40f8-8b56-83ad20d13cb4/images/9b52e5f8-1c2d-4e5f-8a9b-0c1d2e3f4a5b.png";

    [Fact]
    public void An_embedded_image_is_labelled_not_named_by_its_base64()
    {
        FigureFile.WrittenName(Png).Should().Be(FigureFile.EmbeddedLabel);
        FigureFile.Subject(FigureFile.EmbeddedLabel).Should().Be("An embedded image");
    }

    [Fact]
    public void An_image_Lilia_stored_is_labelled_not_named_by_its_GUID()
    {
        FigureFile.WrittenName(Upload).Should().Be(FigureFile.UploadedLabel);
        FigureFile.WrittenName("/uploads/user_2wh3/documents/035db167-2769-40f8-8b56-83ad20d13cb4/images/9b52e5f8-1c2d-4e5f-8a9b-0c1d2e3f4a5b.jpg")
            .Should().Be(FigureFile.UploadedLabel, "local storage serves the same key under /uploads/");
        FigureFile.WrittenName("https://cdn.example.org/papers/plot.png").Should().Be("plot.png", "an outside URL keeps its file name");
        FigureFile.Subject("plot.png").Should().Be("The image plot.png");
    }

    [Fact]
    public async Task An_embedded_image_the_PDF_cannot_use_is_reported_under_its_label()
    {
        // Since task 5 the PDF embeds a PNG or JPEG data: image (BatchEmbeddedImagesTests); one it can't use, like a GIF,
        // still prints the "Missing figure" frame, so the warning names it by its label, with the advice that fits.
        var figure = B("figure", new { src = "data:image/gif;base64,R0lGODlhAQABAAAAACw=", caption = "A GIF" });

        var missing = await DocumentCheckService.FindMissingFigures(FigureFile.Sources([figure]), Missing);

        missing.Should().ContainSingle().Which.File.Should().Be(FigureFile.EmbeddedLabel);
        FigureFile.MissingAdvice(FigureFile.EmbeddedLabel).Should().Contain("upload it as a file").And.NotContain("again");
    }

    [Fact]
    public void A_publisher_finding_for_an_embedded_image_prints_no_base64()
    {
        var doc = Doc("IEEEtran");
        var report = PublisherCompliance.WithMissingFiles(PublisherCompliance.Check(doc, doc.Blocks.ToList(), ""),
            [(Guid.NewGuid().ToString(), FigureFile.WrittenName(Png))])!;

        var finding = report.Findings.Single(f => f.Code == "figure_file");
        finding.Message.Should().StartWith("An embedded image can't be found").And.NotContain("base64").And.Contain("upload it as a file");
    }

    [Fact]
    public void Without_a_publisher_there_is_still_no_checklist()
    {
        PublisherCompliance.WithMissingFiles(null, [("x", "lilia-launch-no-such-image")]).Should().BeNull();
    }
}
