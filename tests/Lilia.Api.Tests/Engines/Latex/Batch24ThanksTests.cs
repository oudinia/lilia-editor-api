using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #249 (19 Nov 2026): arXiv 2611.22535's document-level \thanks (funding, and a preliminary version
/// with its \cite), written after \title and before the first \author, was dropped on import.
/// </summary>
public class Batch24ThanksTests
{
    private const string Note = @"This work has been supported by the FWF, grant I-6744-N. \\ A preliminary version of this work was published in~\cite{AzzouzBodiniDurandGittenberger2026}.";

    [Fact]
    public async Task The_papers_own_note_after_the_title_is_kept()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{amsart}
\title[Number of histories]{Asymptotic enumeration of histories}
\thanks{This work has been supported by the FWF, grant I-6744-N. \\
A preliminary version of this work was published in~\cite{AzzouzBodiniDurandGittenberger2026}.}
\author{Nadja Azzouz}
\address{TU Wien, Austria}
\thanks{ORCID Azzouz: 0009-0004-6060-3476.}
\begin{document}
\maketitle
Body.
\end{document}");
        // amsart: a note on the title is the first author's (launch-bot #143 retest), as the title block names the document.
        doc.Title.Should().Be("Asymptotic enumeration of histories");
        doc.Metadata.Author.Should().Contain(@"Nadja Azzouz\thanks{This work has been supported by the FWF, grant I-6744-N. A preliminary version of this work was published in \cite{AzzouzBodiniDurandGittenberger2026}.}")
            .And.Contain(@"\thanks{ORCID Azzouz: 0009-0004-6060-3476.}");
    }

    [Fact]
    public async Task In_article_the_note_stays_on_the_title()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\title{Histories}
\thanks{Supported by the FWF.}
\author{Nadja Azzouz}
\begin{document}\maketitle Body.\end{document}");
        doc.Title.Should().Be(@"Histories \thanks{Supported by the FWF.}");
        LatexText.Plain(doc.Title).Should().Be("Histories");
    }

    [Fact]
    public void A_note_keeps_its_citation_and_line_break()
    {
        var title = @"Histories \thanks{" + Note + "}";
        foreach (var latex in new[] { LaTeXExportService.FormatTitleMetaLatex(title), RenderService.FormatTitleMetaLatex(title) })
        {
            latex.Should().Contain(@"\cite{AzzouzBodiniDurandGittenberger2026}").And.Contain(@"grant I-6744-N. \\ A preliminary");
            latex.Should().Contain("published in~").And.NotContain("textbackslash");
        }
    }

    [Fact]
    public void Amsart_writes_the_note_after_the_title()
    {
        var titleLatex = LaTeXExportService.FormatTitleMetaLatex(@"Histories \thanks{" + Note + "}");
        var (pre, _) = PublisherClasses.FrontMatter("amsart", "Histories", "Nadja Azzouz", null, [], s => s, titleLatex,
            titleRunning: "Number of histories");
        pre.Should().Contain("\\title[Number of histories]{Histories}\n\\thanks{This work has been supported");
    }
}
