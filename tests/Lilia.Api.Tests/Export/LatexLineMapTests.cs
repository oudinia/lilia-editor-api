using FluentAssertions;
using Lilia.Core.Blocks;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P2.3 groundwork — turning "on input line 391" into a block.
///
/// <para>P0.2 made page overflow visible, but what it surfaces is a line number
/// in a generated file the author has never seen. Until that becomes a block,
/// neither of the two things worth doing is possible: telling them *which* table
/// is too tall, or re-emitting *that one* as a <c>longtable</c>.</para>
///
/// <para>The failure mode being guarded is attribution to the wrong block —
/// pointing an author at innocent content is worse than saying nothing, which
/// is why unpositioned warnings are dropped rather than assigned to whatever
/// came last.</para>
/// </summary>
public class LatexLineMapTests
{
    private static readonly Guid BlockA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid BlockB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    /// <summary>Shaped like the real assembly: preamble, then marked blocks.</summary>
    private static readonly string Document = string.Join("\n",
    [
        @"\documentclass{article}",     // 1
        @"\begin{document}",            // 2
        $"% block:{BlockA}",            // 3
        $@"\label{{blk-{BlockA}}}",     // 4
        @"Some paragraph text.",        // 5
        "",                             // 6
        $"% block:{BlockB}",            // 7
        $@"\label{{blk-{BlockB}}}",     // 8
        @"\begin{table}[htbp]",         // 9
        @"\end{table}",                 // 10
        @"\end{document}",              // 11
    ]);

    [Fact]
    public void Every_block_marker_is_located()
    {
        LatexLineMap.Parse(Document).Count.Should().Be(2);
    }

    [Fact]
    public void A_line_inside_a_block_maps_to_that_block()
    {
        var map = LatexLineMap.Parse(Document);

        // TeX reports where it noticed the problem — inside the body, not on the
        // marker — so this is the case that actually occurs.
        map.BlockAt(5).Should().Be(BlockA);
        map.BlockAt(9).Should().Be(BlockB);
        map.BlockAt(10).Should().Be(BlockB);
    }

    [Fact]
    public void The_marker_line_itself_maps_to_its_own_block()
    {
        LatexLineMap.Parse(Document).BlockAt(3).Should().Be(BlockA);
    }

    [Fact]
    public void A_preamble_line_belongs_to_no_block()
    {
        // Warnings from package loading must not be blamed on the first block.
        LatexLineMap.Parse(Document).BlockAt(1).Should().BeNull();
    }

    [Fact]
    public void A_line_past_the_end_belongs_to_the_last_block()
    {
        LatexLineMap.Parse(Document).BlockAt(999).Should().Be(BlockB);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\documentclass{article}")]
    public void A_document_with_no_markers_maps_nothing(string? latex)
    {
        var map = LatexLineMap.Parse(latex);
        map.Count.Should().Be(0);
        map.BlockAt(1).Should().BeNull();
    }

    [Fact]
    public void A_comment_that_only_looks_like_a_marker_is_ignored()
    {
        // Author-written prose in a raw-LaTeX block could say anything.
        var latex = string.Join("\n",
        [
            "% block: not-a-guid",
            "% blocked: something",
            "% block:" + BlockA,
        ]);

        var map = LatexLineMap.Parse(latex);
        map.Count.Should().Be(1);
        map.BlockAt(3).Should().Be(BlockA);
    }

    // ── Attribution ───────────────────────────────────────────────────

    [Fact]
    public void An_overflow_warning_names_the_block_that_caused_it()
    {
        // The real shape, from the P0.2 investigation.
        var warnings = new[] { "LaTeX Warning: Float too large for page by 1161.16606pt on input line 9." };

        LatexLineMap.Parse(Document).BlocksNamedBy(warnings)
            .Should().ContainSingle().Which.Should().Be(BlockB);
    }

    [Fact]
    public void A_warning_with_no_line_number_names_nobody()
    {
        // Several LaTeX warnings genuinely carry no position. Assigning one to
        // the last block seen would point the author at innocent content.
        var warnings = new[] { "LaTeX Warning: There were undefined references." };

        LatexLineMap.Parse(Document).BlocksNamedBy(warnings).Should().BeEmpty();
    }

    [Fact]
    public void Repeated_warnings_for_one_block_name_it_once()
    {
        // A tall table emits an overfull box per page it runs over.
        var warnings = new[]
        {
            "Overfull \\vbox (525.0pt too high) has occurred while \\output is active on input line 9.",
            "Overfull \\vbox (525.0pt too high) has occurred while \\output is active on input line 10.",
        };

        LatexLineMap.Parse(Document).BlocksNamedBy(warnings).Should().ContainSingle();
    }

    [Fact]
    public void Blocks_are_named_in_the_order_the_warnings_appear()
    {
        var warnings = new[]
        {
            "Overfull \\vbox on input line 9.",
            "Overfull \\vbox on input line 5.",
        };

        LatexLineMap.Parse(Document).BlocksNamedBy(warnings)
            .Should().Equal(BlockB, BlockA);
    }

    [Fact]
    public void A_preamble_warning_names_nobody()
    {
        var warnings = new[] { "Package foo Warning: something on input line 1." };

        LatexLineMap.Parse(Document).BlocksNamedBy(warnings).Should().BeEmpty();
    }
}
