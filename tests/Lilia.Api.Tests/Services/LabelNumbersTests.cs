using FluentAssertions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>The numbers a compile gave a document's labels, as kept between compiles.</summary>
public class LabelNumbersTests
{
    [Fact]
    public void Keeps_the_authors_labels_and_round_trips()
    {
        var stored = LabelNumbers.FromAux(@"\newlabel{tab:results}{{3}{7}}
\newlabel{sec:method}{{2.1}{4}}");

        var back = LabelNumbers.Parse(stored);

        back["tab:results"].Number.Should().Be("3");
        back["tab:results"].Page.Should().Be(7);
        back["sec:method"].Number.Should().Be("2.1");
    }

    [Fact]
    public void Leaves_out_the_page_map_labels()
    {
        // blk-… labels are ours, one per block, and nothing references them.
        var stored = LabelNumbers.FromAux($@"\newlabel{{blk-{Guid.Empty}}}{{{{1}}{{1}}}}
\newlabel{{tab:x}}{{{{1}}{{1}}}}");

        LabelNumbers.Parse(stored).Keys.Should().Equal("tab:x");
    }

    [Fact]
    public void A_compile_with_no_author_labels_stores_nothing()
    {
        // Null, so the caller keeps the last good numbers instead of blanking
        // them with an empty map.
        LabelNumbers.FromAux($@"\newlabel{{blk-{Guid.Empty}}}{{{{1}}{{1}}}}").Should().BeNull();
        LabelNumbers.FromAux(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void Anything_unreadable_is_no_numbers_not_an_error(string? stored)
    {
        LabelNumbers.Parse(stored).Should().BeEmpty();
    }
}
