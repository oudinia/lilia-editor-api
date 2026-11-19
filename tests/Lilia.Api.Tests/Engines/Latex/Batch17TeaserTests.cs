using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// acmart's teaser (launch-bot #242 retest, 19 Nov 2026): the figure under the title was raw LaTeX after \maketitle, so
/// it never printed, and its image was dropped. Now a figure marked as the teaser, before \maketitle under acmart.
/// </summary>
public class Batch17TeaserTests
{
    [Fact]
    public async Task A_teaserfigure_imports_as_a_figure_marked_as_the_teaser()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass[sigconf]{acmart}
\begin{document}
\title{The Name of the Title}
\begin{teaserfigure}
  \includegraphics[width=\textwidth]{sampleteaser}
  \caption{Seattle Mariners at Spring Training, 2010.}
  \Description{Enjoying the baseball game from the third-base seats.}
  \label{fig:teaser}
\end{teaserfigure}
\maketitle
\section{Introduction}
Text.
\end{document}");
        var figure = doc.Elements.OfType<ImportImage>().Single();
        figure.Filename.Should().Be("sampleteaser");
        figure.Span.Should().Be("teaser");
        figure.Label.Should().Be("fig:teaser");
        doc.Elements.OfType<ImportLatexPassthrough>().Should().NotContain(p => p.LatexCode.Contains("teaserfigure"));

        var content = LatexImportJobExecutor.FigureContent(figure);
        content["teaser"].Should().Be(true);
        content["span"].Should().Be("page");
    }

    private static Document WithTeaser(string cls)
    {
        var doc = Doc(cls, cls == "acmart" ? "sigconf" : null);
        var teaser = B("figure", new { src = "", caption = "Seattle Mariners.", label = "fig:teaser", span = "page", teaser = true });
        var blocks = doc.Blocks.ToList();
        blocks.Insert(2, teaser);
        doc.Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList();
        return doc;
    }

    private static string Preview(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    private static string Export(Document doc) => new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
        .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });

    [Fact]
    public void Under_acmart_the_teaser_comes_before_maketitle_once()
    {
        foreach (var tex in new[] { Preview(WithTeaser("acmart")), Export(WithTeaser("acmart")) })
        {
            var at = tex.IndexOf(@"\begin{teaserfigure}", StringComparison.Ordinal);
            at.Should().BeGreaterThan(0);
            at.Should().BeLessThan(tex.IndexOf(@"\maketitle", StringComparison.Ordinal));
            tex.Should().Contain(@"\end{teaserfigure}").And.Contain("Seattle Mariners.");
            tex.Split("Seattle Mariners.").Length.Should().Be(2, "written once, not in the body as well");
            tex.Should().NotContain(@"\begin{figure*}");
        }
    }

    [Fact]
    public void Under_another_class_the_teaser_is_a_wide_figure_in_the_body()
    {
        foreach (var tex in new[] { Preview(WithTeaser("article")), Export(WithTeaser("article")) })
        {
            tex.Should().NotContain("teaserfigure");
            tex.Should().Contain(@"\begin{figure*}").And.Contain("Seattle Mariners.");
        }
    }

    [Fact]
    public void The_figure_becomes_a_teaserfigure_on_the_line_before_maketitle()
    {
        TeaserFigure.AsTeaser("\\begin{figure*}[t]\n\\centering\nX\n\\end{figure*}")
            .Should().Be("\\begin{teaserfigure}\n\\centering\nX\n\\end{teaserfigure}");
        TeaserFigure.BeforeMaketitle("\\begin{abstract}A\\end{abstract}\n\\maketitle\n", "T")
            .Should().Be("\\begin{abstract}A\\end{abstract}\nT\n\\maketitle\n");
    }
}
