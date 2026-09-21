using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The cross-reference index — what a document defines, what it points at, and
/// where those disagree.
///
/// <para>The cases worth pinning are the ones that ship broken PDFs quietly: a
/// <c>\ref</c> to a key nothing defines sets <c>??</c> and compiles
/// successfully, and a duplicate label makes LaTeX take the last one without
/// saying so. Neither is visible in a diff and neither fails a build.</para>
/// </summary>
public class ReferenceIndexTests
{
    private static int _order;

    private static Block Block(string type, string json) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(json),
    };

    private static Block Table(string label, string caption = "Results") =>
        Block(BlockTypes.Table, $$"""{"label":"{{label}}","caption":"{{caption}}"}""");

    private static Block Prose(string text) =>
        Block(BlockTypes.Paragraph, JsonSerializer.Serialize(new { text }));

    [Fact]
    public void Finds_a_label_and_the_thing_that_points_at_it()
    {
        var report = ReferenceIndex.Build([Table("tab:results"), Prose(@"See \ref{tab:results}.")]);

        report.Targets.Should().ContainSingle();
        report.Targets[0].Key.Should().Be("tab:results");
        report.Targets[0].Kind.Should().Be(ReferenceKind.Table);
        report.Targets[0].Caption.Should().Be("Results");

        report.Uses.Should().ContainSingle();
        report.Uses[0].Form.Should().Be("ref");

        report.Problems.Should().BeEmpty();
    }

    [Fact]
    public void A_reference_to_nothing_is_reported_as_dangling()
    {
        // The one that costs the author: LaTeX sets "??" and exits 0.
        var report = ReferenceIndex.Build([Prose(@"As shown in \ref{fig:missing}.")]);

        var dangling = report.Problems.Should().ContainSingle(p => p.Kind == "dangling").Subject;
        dangling.Key.Should().Be("fig:missing");
    }

    [Fact]
    public void Two_blocks_claiming_one_key_is_reported_as_duplicate()
    {
        // LaTeX silently keeps the last, so half the references point somewhere
        // the author did not mean.
        var report = ReferenceIndex.Build([Table("tab:x"), Table("tab:x"), Prose(@"\ref{tab:x}")]);

        var dup = report.Problems.Should().ContainSingle(p => p.Kind == "duplicate").Subject;
        dup.BlockIds.Should().HaveCount(2);
    }

    [Fact]
    public void A_label_nobody_points_at_is_information_not_an_error()
    {
        var report = ReferenceIndex.Build([Table("tab:draft")]);

        report.Problems.Should().ContainSingle(p => p.Kind == "unused");
        report.Problems.Should().NotContain(p => p.Kind == "dangling");
    }

    [Fact]
    public void Citations_are_not_references()
    {
        // Folding \cite in here would report every citation in the document as a
        // dangling reference, because its target lives in the bibliography.
        var report = ReferenceIndex.Build([Prose(@"As \cite{knuth1984} showed.")]);

        report.Uses.Should().BeEmpty();
        report.Problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"\ref{k}", "ref")]
    [InlineData(@"\eqref{k}", "eqref")]
    [InlineData(@"\cref{k}", "cref")]
    [InlineData(@"\Cref{k}", "Cref")]
    [InlineData(@"\autoref{k}", "autoref")]
    [InlineData("@ref{k}", "at")]
    public void Every_form_of_pointing_is_recognised(string written, string form)
    {
        var report = ReferenceIndex.Build([Prose($"text {written} text")]);

        report.Uses.Should().ContainSingle();
        report.Uses[0].Key.Should().Be("k");
        report.Uses[0].Form.Should().Be(form);
    }

    [Fact]
    public void A_reference_inside_a_table_cell_is_found()
    {
        // Scanning the raw JSON rather than knowing where each block type keeps
        // its prose is the point: a \ref lives in cells and captions too.
        var cell = Block(BlockTypes.Table,
            """{"label":"tab:a","rows":[[{"content":"see \\ref{fig:b}"}]]}""");

        var report = ReferenceIndex.Build([cell]);

        report.Uses.Should().ContainSingle(u => u.Key == "fig:b");
    }

    [Fact]
    public void Subfigure_labels_are_targets_in_their_own_right()
    {
        var figure = Block(BlockTypes.Figure,
            """{"label":"fig:all","subfigures":[{"label":"fig:a","caption":"Left"}]}""");

        var report = ReferenceIndex.Build([figure]);

        report.Targets.Select(t => t.Key).Should().BeEquivalentTo(["fig:all", "fig:a"]);
        report.Targets.Should().ContainSingle(t => t.Key == "fig:a" && t.Caption == "Left");
    }

    [Fact]
    public void Block_kind_comes_from_the_block_not_from_a_stored_field()
    {
        // The current editor panel stores a labelType separately, which is how
        // it gets to disagree with the block it describes.
        var report = ReferenceIndex.Build([
            Table("tab:a"),
            Block(BlockTypes.Equation, """{"label":"eq:a"}"""),
            Block(BlockTypes.Heading, """{"label":"sec:a"}"""),
        ]);

        report.Targets.Should().Contain(t => t.Key == "eq:a" && t.Kind == ReferenceKind.Equation);
        report.Targets.Should().Contain(t => t.Key == "sec:a" && t.Kind == ReferenceKind.Section);
    }

    [Fact]
    public void Numbers_come_from_the_aux_when_there_has_been_a_compile()
    {
        var aux = @"\newlabel{tab:results}{{3}{7}{Top-1 accuracy}{table.3}{}}";

        var report = ReferenceIndex.Build([Table("tab:results")], aux);

        report.Targets[0].Number.Should().Be("3");
        report.Targets[0].Page.Should().Be(7);
    }

    [Fact]
    public void Without_a_compile_the_number_is_null_rather_than_a_guess()
    {
        // Counting floats here would produce a 1 that looks authoritative and is
        // wrong the moment \numberwithin or an appendix is involved. "Unchecked"
        // is not "invalid" — the same rule the table tool's verdict follows.
        var report = ReferenceIndex.Build([Table("tab:results")]);

        report.Targets[0].Number.Should().BeNull();
        report.Targets[0].Page.Should().BeNull();
    }

    [Fact]
    public void An_empty_label_defines_nothing()
    {
        // Every new table block ships with "label": "".
        var report = ReferenceIndex.Build([Table("")]);

        report.Targets.Should().BeEmpty();
        report.Problems.Should().BeEmpty();
    }
}
