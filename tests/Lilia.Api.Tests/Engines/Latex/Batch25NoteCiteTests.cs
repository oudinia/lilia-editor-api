using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #249 retest (19 Nov 2026): arXiv 2611.22535's funding note printed on page 1, but its citation was
/// lost on the way out, "published in ." (the stored author text still held \cite{AzzouzBodiniDurandGittenberger2026}).
/// </summary>
public class Batch25NoteCiteTests
{
    private const string Author = @"Nadja Azzouz\thanks{This work has been supported by the FWF. A preliminary version of this work was published in \cite{AzzouzBodiniDurandGittenberger2026}.}\thanks{ORCID Azzouz: 0009-0004-6060-3476.}\\TU Wien, Austria";

    [Fact]
    public void An_authors_note_keeps_its_citation_in_the_title_block()
    {
        TitleAuthors.Parse(Author).Single().Notes.Should().Contain(
            @"This work has been supported by the FWF. A preliminary version of this work was published in \cite{AzzouzBodiniDurandGittenberger2026}.");
    }

    [Theory]
    [InlineData("amsart")]
    [InlineData("revtex4-2")]
    public void The_publisher_front_matter_prints_the_citation(string cls)
    {
        var (pre, body) = PublisherClasses.FrontMatter(cls, "Histories", Author, null, [], LaTeXExportService.FormatTitleMetaLatex,
            "Histories");
        (pre + body).Should().Contain(@"published in \cite{AzzouzBodiniDurandGittenberger2026}.}").And.NotContain("published in .");
    }
}
