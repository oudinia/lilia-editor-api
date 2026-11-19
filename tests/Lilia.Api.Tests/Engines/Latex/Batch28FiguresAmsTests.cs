using FluentAssertions;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #244 (19 Nov 2026): \includegraphics{example-image-a}, an image TeX Live provides (mwe), became a missing
/// figures/image-0.png and the author was asked to upload it. And its #243 retest notes: amsart's \curraddr printed as a body
/// paragraph, and \date was dropped.
/// </summary>
public class Batch28FiguresAmsTests
{
    [Theory]
    [InlineData("example-image-a")]
    [InlineData("example-image")]
    [InlineData("example-image-golden")]
    [InlineData("example-image-16x9")]
    public void A_tex_live_image_is_included_as_written(string name)
    {
        FigureFile.Name(name).Should().Be(name);
        FigureFile.Include(FigureFile.Name(name), @"0.8\textwidth").Should().Be($@"\includegraphics[width=0.8\textwidth]{{{name}}}");
        FigureFile.Sources([B("figure", new { src = name, caption = "x" })]).Should().BeEmpty("there is no file to export or look for");
    }

    [Fact]
    public void A_figure_with_a_tex_live_image_prints_it()
    {
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance)
            .RenderBlockToLatex(B("figure", new { src = "example-image-a", caption = "An example." }));
        latex.Should().Contain("{example-image-a}").And.NotContain("Missing figure").And.NotContain("figures/");
    }

    [Fact]
    public void An_ordinary_image_is_unchanged()
    {
        FigureFile.Name("example-imagery.png").Should().Be("example-imagery.png");
        FigureFile.Include("photo.png", @"\textwidth").Should().Contain(@"\IfFileExists{figures/photo.png}");
    }

    [Fact]
    public async Task Amsart_keeps_the_current_address_and_the_date()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{amsart}
\begin{document}
\title{Sample}
\author{Jane Doe}
\address{Louisiana State University}
\curraddr{Department of Mathematics, Rice University}
\email{jane@example.edu}
\date{January 1, 2001}
\maketitle
Body.
\end{document}");
        doc.Metadata.Author.Should().Contain("Current address: Department of Mathematics, Rice University");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("Rice University"));

        var (pre, _) = PublisherClasses.FrontMatter("amsart", "Sample", doc.Metadata.Author, null, [], s => s, "Sample",
            date: "January 1, 2001");
        pre.Should().Contain(@"\address{Louisiana State University}").And.Contain(@"\curraddr{Department of Mathematics, Rice University}")
            .And.Contain(@"\date{January 1, 2001}").And.NotContain("Current address:");
    }
}
