using System.Text.Json;
using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// launch-bot #221 retest (22 Nov 2026): get_latex's bibliography (Lilia's own, inline) printed an article without its
/// volume, pages or DOI, while the compile from references.bib printed them.
/// </summary>
public class InlineBibliographyFieldsTests
{
    private static JsonElement E(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void An_article_names_its_volume_number_pages_and_DOI()
    {
        var text = RenderService.BibItemText(E(new
        {
            author = "Albert Einstein", title = "Zur Elektrodynamik bewegter K{\\\"o}rper", journal = "Annalen der Physik",
            volume = "322", number = "10", pages = "891-921", year = "1905", doi = "10.1002/andp.19053221004",
        }));
        text.Should().Contain("Annalen der Physik 322(10)").And.Contain("pp.~891--921").And.Contain("(1905)")
            .And.EndWith("doi:10.1002/andp.19053221004.");
    }

    [Fact]
    public void A_book_names_its_publisher_and_a_DOI_with_an_underscore_is_escaped()
    {
        var text = RenderService.BibItemText(E(new { author = "D. Knuth", title = "The TeXbook", publisher = "Addison-Wesley", year = "1984", doi = "10.1000/a_b" }));
        text.Should().Contain("Addison-Wesley").And.Contain(@"doi:10.1000/a\_b");
    }

    [Fact]
    public void What_it_printed_before_is_unchanged_when_there_is_nothing_more()
    {
        RenderService.BibItemText(E(new { author = "A. Author", title = "A title", journal = "J", year = "2020" }))
            .Should().Be(@"A. Author, \textit{A title}, J, (2020).");
        RenderService.BibItemText(E(new { eprint = "1612.01514", archiveprefix = "arXiv", title = "T" }))
            .Should().Contain("arXiv:1612.01514");
    }
}
