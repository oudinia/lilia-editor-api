using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P2.2 — page-break intent as block attributes instead of positional
/// <c>@pagebreak</c> blocks.
///
/// <para>The point is decay. A manual break says "break here", not why; add a
/// paragraph above it and every downstream break is wrong while still looking
/// deliberate. An attribute records intent, so it stays correct across edits and
/// LaTeX recomputes the consequence.</para>
///
/// <para>Every construct asserted here was compile-verified with pdflatex before
/// being emitted — <c>\Needspace</c>, <c>samepage</c>, <c>\clearpage</c>,
/// <c>[H]</c> — so these tests pin behaviour that is known to work in a real
/// document, not merely to look right in a string.</para>
/// </summary>
public class SemanticBreakTests
{
    private static readonly RenderService Render = BuildRenderService();

    private static RenderService BuildRenderService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RenderService(new LiliaDbContext(opts), NullLogger<RenderService>.Instance);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // ── Individual attributes ─────────────────────────────────────────

    [Fact]
    public void No_attributes_emit_nothing()
    {
        // The overwhelmingly common case. A block with no stated intent must add
        // nothing at all to the document.
        BlockBreakAttributes.For(Json("""{"text":"hello"}""")).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void StartsOnNewPage_clears_the_page_first()
    {
        var w = BlockBreakAttributes.For(Json("""{"startsOnNewPage":true}"""));

        // \clearpage, not \newpage: pending floats are flushed first, so a figure
        // from the previous section cannot drift past the break and land under
        // the new heading.
        w.Before.Should().Contain(@"\clearpage");
        w.After.Should().BeEmpty();
    }

    [Fact]
    public void KeepWithNext_reserves_space_after_the_block()
    {
        var w = BlockBreakAttributes.For(Json("""{"keepWithNext":true}"""));

        w.Before.Should().Contain(@"\Needspace");
        w.Before.Should().Contain(@"4\baselineskip");
    }

    [Fact]
    public void AvoidBreakInside_wraps_the_block_in_samepage()
    {
        var w = BlockBreakAttributes.For(Json("""{"avoidBreakInside":true}"""));

        w.Before.Should().Contain(@"\begin{samepage}");
        w.After.Should().Contain(@"\end{samepage}");
    }

    [Fact]
    public void Clearpage_is_emitted_before_needspace_not_after()
    {
        // Order is the whole correctness of combining these two: reserving space
        // and THEN clearing the page reserves it on the page being abandoned.
        var w = BlockBreakAttributes.For(
            Json("""{"startsOnNewPage":true,"keepWithNext":true}"""));

        w.Before.IndexOf(@"\clearpage", StringComparison.Ordinal)
            .Should().BeLessThan(w.Before.IndexOf(@"\Needspace", StringComparison.Ordinal));
    }

    [Fact]
    public void All_three_attributes_compose()
    {
        var w = BlockBreakAttributes.For(
            Json("""{"startsOnNewPage":true,"keepWithNext":true,"avoidBreakInside":true}"""));

        w.Before.Should().Contain(@"\clearpage").And.Contain(@"\Needspace").And.Contain(@"\begin{samepage}");
        w.After.Should().Contain(@"\end{samepage}");
    }

    [Theory]
    [InlineData("""{"keepWithNext":false}""")]
    [InlineData("""{"keepWithNext":"true"}""")]  // string, not bool
    [InlineData("""{"keepWithNext":1}""")]       // number, not bool
    [InlineData("[]")]
    [InlineData("null")]
    public void Only_a_real_boolean_true_switches_an_attribute_on(string raw)
    {
        // A truthy-looking string from a sloppy client must not silently
        // repaginate someone's document.
        BlockBreakAttributes.For(Json(raw)).IsEmpty.Should().BeTrue();
    }

    // ── Float placement ───────────────────────────────────────────────

    [Theory]
    [InlineData("here", "[H]")]
    [InlineData("top", "[t]")]
    [InlineData("bottom", "[b]")]
    [InlineData("page", "[p]")]
    [InlineData("auto", "[htbp]")]
    [InlineData("nonsense", "[htbp]")]
    public void Placement_maps_to_a_float_specifier(string placement, string expected)
    {
        BlockBreakAttributes.FloatSpecifier(Json($$"""{"placement":"{{placement}}"}"""))
            .Should().Be(expected);
    }

    [Fact]
    public void Absent_placement_defaults_to_auto()
    {
        BlockBreakAttributes.FloatSpecifier(Json("{}")).Should().Be("[htbp]");
    }

    // ── Reaching the emitted document ─────────────────────────────────

    private static Block TableBlock(string placement) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = "table",
        SortOrder = 0,
        Content = JsonDocument.Parse(
            $$"""{"placement":"{{placement}}","headers":["A"],"rows":[["1"]]}"""),
    };

    [Fact]
    public void A_table_now_honours_placement_like_a_figure_does()
    {
        // The defect this closes: tables hard-coded [htbp] and ignored the
        // attribute, so setting "here" on a table did nothing — and said nothing
        // — while the same attribute worked on figures.
        Render.RenderBlockToLatex(TableBlock("here")).Should().Contain(@"\begin{table}[H]");
    }

    [Fact]
    public void A_table_without_placement_is_unchanged()
    {
        Render.RenderBlockToLatex(TableBlock("auto")).Should().Contain(@"\begin{table}[htbp]");
    }

    [Fact]
    public void The_preamble_loads_the_package_Needspace_requires()
    {
        // \Needspace is not a kernel command. Emitting it without needspace
        // loaded fails the compile with "Undefined control sequence" — and the
        // attribute is opt-in, so it would fail only for the documents that used
        // it, long after this shipped.
        LaTeXPreamble.Packages.Should().Contain(@"\usepackage{needspace}");
    }
}
