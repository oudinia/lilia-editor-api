using FluentAssertions;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>#79 (12 Nov 2026): a Markdown or PDF source whose first heading is a subsection printed "0.1".</summary>
public class HeadingLevelsTests
{
    [Fact]
    public void Markdown_starting_at_two_hashes_starts_at_a_section_and_keeps_its_nesting()
    {
        var levels = MarkdownTextParser.Parse("## Results\n\nText.\n\n### Detail\n\nMore.\n\n## Discussion\n")
            .OfType<ImportHeading>().Select(h => h.Level);
        levels.Should().Equal(1, 2, 1);
    }

    [Fact]
    public void A_text_that_already_starts_at_a_section_is_left_as_it_is()
    {
        MarkdownTextParser.Parse("# Intro\n\n## Part\n").OfType<ImportHeading>().Select(h => h.Level).Should().Equal(1, 2);
    }

    [Fact]
    public void Promote_moves_every_heading_up_by_the_same_amount()
    {
        var els = new List<ImportElement> { new ImportHeading { Level = 3, Text = "a" }, new ImportParagraph { Text = "p" }, new ImportHeading { Level = 4, Text = "b" } };
        HeadingLevels.Promote(els).OfType<ImportHeading>().Select(h => h.Level).Should().Equal(1, 2);
    }
}
