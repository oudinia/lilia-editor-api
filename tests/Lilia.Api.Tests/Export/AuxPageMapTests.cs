using FluentAssertions;
using Lilia.Core.Blocks;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P2.1 — reading a block → page map out of the LaTeX <c>.aux</c>.
///
/// <para>The cases that matter are the ones where a naive regex gets the wrong
/// answer <b>without failing</b>: a section title containing braces shifts every
/// subsequent group along, so the parser returns a "page" that is really part of
/// a title. A page map that is confidently wrong is worse than one with a hole,
/// because the editor would draw page markers in the wrong places and nothing
/// would look broken.</para>
/// </summary>
public class AuxPageMapTests
{
    private static readonly Guid BlockA = Guid.Parse("7f3a0000-0000-0000-0000-000000000001");
    private static readonly Guid BlockB = Guid.Parse("7f3a0000-0000-0000-0000-000000000002");

    [Fact]
    public void The_page_is_the_second_group()
    {
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{2.1}}{{7}}}}";

        AuxPageMap.Parse(aux).Should().ContainKey(BlockA).WhoseValue.Should().Be(7);
    }

    [Fact]
    public void Hyperrefs_five_group_form_is_read_the_same_way()
    {
        // hyperref is loaded by the standard preamble, so this — not the
        // two-group form — is what real documents produce.
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{2.1}}{{12}}{{Introduction}}{{section.2.1}}{{}}}}";

        AuxPageMap.Parse(aux)[BlockA].Should().Be(12);
    }

    [Fact]
    public void A_title_containing_braces_does_not_shift_the_page()
    {
        // The case a flat regex gets wrong. `\textbf{Heavy}` opens and closes a
        // brace inside the third group; counting depth keeps the groups aligned,
        // scanning for the next `}` does not.
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{3}}{{42}}{{A \textbf{{Heavy}} Title}}{{section.3}}{{}}}}";

        AuxPageMap.Parse(aux)[BlockA].Should().Be(42);
    }

    [Fact]
    public void Nested_braces_several_deep_are_still_handled()
    {
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{1}}{{5}}{{{{a{{b{{c}}}}}}}}{{section.1}}{{}}}}";

        AuxPageMap.Parse(aux)[BlockA].Should().Be(5);
    }

    [Fact]
    public void An_escaped_brace_in_a_title_is_literal_text()
    {
        // \{ is a printed brace, not structure. Treating it as structure would
        // unbalance the count and swallow the rest of the file.
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{1}}{{9}}{{literal \{{ brace}}{{section.1}}{{}}}}"
                + "\n" + $@"\newlabel{{blk-{BlockB}}}{{{{2}}{{10}}}}";

        var map = AuxPageMap.Parse(aux);
        map[BlockA].Should().Be(9);
        map[BlockB].Should().Be(10, "a mis-counted escape would have eaten this entry");
    }

    [Fact]
    public void Every_block_in_a_realistic_aux_is_found()
    {
        var aux = $@"\relax
\providecommand\hyper@newdestlabel[2]{{}}
\newlabel{{blk-{BlockA}}}{{{{1}}{{1}}{{}}{{section*.1}}{{}}}}
\@writefile{{toc}}{{\contentsline {{section}}{{\numberline {{1}}Intro}}{{1}}{{}}}}
\newlabel{{blk-{BlockB}}}{{{{2}}{{3}}{{}}{{section*.2}}{{}}}}
\gdef \@abspage@last{{3}}";

        var map = AuxPageMap.Parse(aux);
        map.Should().HaveCount(2);
        map[BlockA].Should().Be(1);
        map[BlockB].Should().Be(3);
    }

    [Fact]
    public void Labels_that_are_not_ours_are_ignored()
    {
        // Author-written labels share the file. `\@writefile` lines also contain
        // brace groups that look superficially similar.
        var aux = $@"\newlabel{{eq:main}}{{{{1}}{{4}}}}
\newlabel{{fig:plot}}{{{{2}}{{5}}}}
\newlabel{{blk-{BlockA}}}{{{{3}}{{6}}}}";

        var map = AuxPageMap.Parse(aux);
        map.Should().HaveCount(1);
        map[BlockA].Should().Be(6);
    }

    [Fact]
    public void A_blk_label_that_is_not_a_guid_is_skipped()
    {
        AuxPageMap.Parse(@"\newlabel{blk-not-a-guid}{{1}{2}}").Should().BeEmpty();
    }

    [Theory]
    [InlineData("iv")]     // roman front matter
    [InlineData("A-3")]    // \thepage redefined
    [InlineData("")]
    public void A_non_numeric_page_is_skipped_rather_than_guessed(string page)
    {
        // Front matter really is numbered in roman, so this is not hypothetical.
        // Dropping the entry leaves a hole; inventing a number would put a page
        // marker in the wrong place and look correct.
        AuxPageMap.Parse($@"\newlabel{{blk-{BlockA}}}{{{{1}}{{{page}}}}}")
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\relax")]
    [InlineData(@"\newlabel{blk-")]          // truncated mid-name
    public void Empty_or_truncated_input_yields_an_empty_map(string? aux)
    {
        AuxPageMap.Parse(aux).Should().BeEmpty();
    }

    [Fact]
    public void A_malformed_entry_does_not_stop_the_ones_after_it()
    {
        // An unterminated value would end the scan if the parser gave up
        // globally instead of per entry.
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{1}}
\newlabel{{blk-{BlockB}}}{{{{2}}{{8}}}}";

        AuxPageMap.Parse(aux).Should().ContainKey(BlockB);
    }

    [Fact]
    public void A_redefined_label_takes_its_last_value()
    {
        var aux = $@"\newlabel{{blk-{BlockA}}}{{{{1}}{{2}}}}
\newlabel{{blk-{BlockA}}}{{{{1}}{{9}}}}";

        AuxPageMap.Parse(aux)[BlockA].Should().Be(9);
    }

    /// <summary>
    /// Verbatim .aux from a real pdflatex run (MiKTeX, 2026-07-31) over a
    /// document shaped exactly as <c>RenderToLatexAsync</c> emits: hyperref
    /// loaded, a `\label{blk-…}` before each block, five pages of lipsum.
    ///
    /// Two things here were not in the synthetic cases and are worth pinning:
    /// the first block's reference group is <b>empty</b> (`{{}{1}…`) because no
    /// counter had stepped when the label was written, and LaTeX writes
    /// `\textbf {Heavy}` — with a space before the brace — into the title group.
    /// </summary>
    private const string RealAux = """
        \newlabel{blk-7f3a0000-0000-0000-0000-000000000001}{{}{1}{}{Doc-Start}{}}
        \newlabel{blk-7f3a0000-0000-0000-0000-000000000002}{{1}{1}{A \textbf {Heavy} Title}{section.1}{}}
        \newlabel{blk-7f3a0000-0000-0000-0000-000000000003}{{1}{4}{A \textbf {Heavy} Title}{section.1}{}}
        """;

    [Fact]
    public void A_real_pdflatex_aux_parses_to_the_pages_the_pdf_actually_has()
    {
        var map = AuxPageMap.Parse(RealAux);

        map.Should().HaveCount(3);
        map[BlockA].Should().Be(1, "an empty reference group must not shift the page");
        map[BlockB].Should().Be(1);
        map[Guid.Parse("7f3a0000-0000-0000-0000-000000000003")].Should().Be(4);
    }

    [Fact]
    public void The_label_we_emit_is_the_label_we_parse()
    {
        // Guards the two halves against drifting apart — the emitter and the
        // parser agree only because they share this one function.
        var label = AuxPageMap.LabelFor(BlockA);

        AuxPageMap.Parse($@"\newlabel{{{label}}}{{{{1}}{{3}}}}")[BlockA].Should().Be(3);
    }
}
