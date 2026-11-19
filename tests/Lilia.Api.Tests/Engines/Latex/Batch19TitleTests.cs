using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #246 (19 Nov 2026): REVTeX's \title[Sample title]{Sample Title:\\with Forced Linebreak\footnote{Error!}}
/// lost its short title, and its \footnote was escaped into printed text.
/// </summary>
public class Batch19TitleTests
{
    private const string Revtex = @"\documentclass[aip,jmp]{revtex4-2}
\begin{document}
\title[Sample title]{Sample Title:\\with Forced Linebreak\footnote{Error!}}
\maketitle
\section{Intro}
Text.
\end{document}";

    [Fact]
    public async Task The_short_title_and_the_footnote_are_kept_on_import()
    {
        var doc = await new LatexParser().ParseTextAsync(Revtex);
        doc.Metadata.TitleRunning.Should().Be("Sample title");
        doc.Title.Should().Contain(@"\footnote{Error!}");
    }

    [Fact]
    public void A_footnote_in_a_title_stays_a_command()
    {
        const string title = @"Sample Title:\\with Forced Linebreak\footnote{Error!}";
        LaTeXExportService.FormatTitleMetaLatex(title).Should().Contain(@"\footnote{Error!}").And.NotContain("textbackslash");
        RenderService.FormatTitleMetaLatex(title).Should().Contain(@"\footnote{Error!}").And.NotContain("textbackslash");
    }

    [Theory]
    [InlineData("revtex4-2")]
    [InlineData("acmart")]
    [InlineData("amsart")]
    public void The_short_title_is_written_as_the_classes_optional_argument(string cls)
    {
        var (pre, body) = PublisherClasses.FrontMatter(cls, "Sample Title", "Ada Lovelace", null, [], s => s, "Sample Title",
            titleRunning: "Sample title");
        (pre + body).Should().Contain(@"\title[Sample title]{Sample Title}");
    }

    [Fact]
    public void Without_a_short_title_the_title_is_as_before()
    {
        var (pre, body) = PublisherClasses.FrontMatter("revtex4-2", "Sample Title", "Ada Lovelace", null, [], s => s, "Sample Title");
        (pre + body).Should().Contain(@"\title{Sample Title}");
    }
}
