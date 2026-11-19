using FluentAssertions;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's llncs paper (arXiv 2610.20170, 18 Nov 2026, #203): [runningheads] was dropped, \titlerunning and
/// \authorrunning printed as body text, ORCIDs were lost, and \inst{1,2} became one merged institute.
/// </summary>
public class Batch11LlncsFrontMatterTests
{
    private const string Source = """
        \documentclass[runningheads]{llncs}
        \begin{document}
        \title{A Long Title for the Front Matter Test}
        \titlerunning{Short Title}
        \author{Ann First\inst{1}\orcidID{0000-0001-2345-6789} \and
        Bob Second\inst{1,2}\orcidID{0000-0002-3456-7890} \and
        Cy Third\inst{3}}
        \authorrunning{A. First et al.}
        \institute{First Institute \and
        Second Lab \and
        Third School\\ Some University, Country}
        \maketitle
        \begin{abstract}The abstract.\end{abstract}
        \section{Intro}
        Body.
        \end{document}
        """;

    [Fact]
    public async Task Running_heads_go_to_the_title_block_not_the_body()
    {
        var doc = await new LatexParser().ParseTextAsync(Source);
        doc.Metadata.TitleRunning.Should().Be("Short Title");
        doc.Metadata.AuthorRunning.Should().Be("A. First et al.");
        doc.Elements.OfType<ImportParagraph>().Should().NotContain(p => p.Text.Contains("Short Title") || p.Text.Contains("First et al."));
    }

    [Fact]
    public void The_reader_keeps_orcids_and_both_institutes()
    {
        var front = PublisherFrontMatterReader.Read(Source, "llncs")!;
        var bob = front.Authors.Single(a => a.Name.StartsWith("Bob"));
        bob.Name.Should().Contain(@"\orcidlink{0000-0002-3456-7890}");
        bob.Lines.Should().Equal("First Institute", "Second Lab");
    }

    [Fact]
    public void The_llncs_front_matter_has_multiple_institutes_orcids_and_running_heads()
    {
        var front = PublisherFrontMatterReader.Read(Source, "llncs")!;
        var (pre, _) = PublisherClasses.FrontMatter("llncs", "A Long Title", front.AuthorText, null, [], s => s, "A Long Title",
            titleRunning: "Short Title", authorRunning: "A. First et al.");
        pre.Should().Contain(@"Ann First\inst{1}\orcidID{0000-0001-2345-6789}");
        pre.Should().Contain(@"Bob Second\inst{1,2}\orcidID{0000-0002-3456-7890}");
        pre.Should().Contain(@"Cy Third\inst{3}");
        pre.Should().Contain(@"\institute{First Institute \and Second Lab \and Third School, Some University, Country}");
        pre.Should().Contain(@"\titlerunning{Short Title}").And.Contain(@"\authorrunning{A. First et al.}");
    }

    [Fact]
    public void Llncs_keeps_runningheads_from_the_source()
    {
        PublisherProfiles.WithSourceOptions("llncs", null, "runningheads").Should().Be("runningheads");
        // Since launch-bot #240 (19 Nov 2026) every source option is kept as written, once, for any publisher class: a4paper
        // too, and the venue's own options after the source's.
        PublisherProfiles.WithSourceOptions("llncs", "runningheads", "runningheads,a4paper").Should().Be("runningheads,a4paper");
        PublisherProfiles.WithSourceOptions("IEEEtran", "conference", "conference,10pt").Should().Be("conference,10pt");
        PublisherProfiles.WithSourceOptions("article", "draft", "runningheads").Should().Be("draft", "not a publisher class: untouched");
    }
}
