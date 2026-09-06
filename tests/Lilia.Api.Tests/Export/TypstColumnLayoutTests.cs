using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// columnLayout in Typst, closed 2026-09-06.
///
/// <para>The bug these pin: Typst had no arm for the block, Typst is the default
/// PDF engine, so a multi-column block became an invisible comment and the
/// columns silently did not happen. Silent content loss on the common path.</para>
///
/// <para>The fix has two halves and they behave differently on purpose, so both
/// are asserted here rather than left to be rediscovered.</para>
/// </summary>
public class TypstColumnLayoutTests
{
    private static TypstRenderService Service()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new TypstRenderService(new LiliaDbContext(opts),
                                      NullLogger<TypstRenderService>.Instance);
    }

    private static Block Block(string type, string json, int order) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.Empty,
        Type = type,
        SortOrder = order,
        Content = JsonDocument.Parse(json),
    };

    // ── the per-block arm ────────────────────────────────────────────────

    [Fact]
    public void Start_marker_alone_sets_page_columns()
    {
        var typst = Service().RenderBlockToTypst(
            Block("columnLayout", """{"mode":"start","columns":2}""", 0));

        Assert.Equal("#set page(columns: 2)", typst);
    }

    [Fact]
    public void End_marker_alone_restores_one_column()
    {
        var typst = Service().RenderBlockToTypst(
            Block("columnLayout", """{"mode":"end"}""", 0));

        Assert.Equal("#set page(columns: 1)", typst);
    }

    [Fact]
    public void Column_count_is_clamped_the_same_way_the_other_emitters_clamp_it()
    {
        var svc = Service();

        // LaTeX clamps to 1..3; the emitters must not disagree about a document.
        Assert.Equal("#set page(columns: 3)",
            svc.RenderBlockToTypst(Block("columnLayout", """{"mode":"start","columns":9}""", 0)));
        Assert.Equal("#set page(columns: 1)",
            svc.RenderBlockToTypst(Block("columnLayout", """{"mode":"start","columns":0}""", 0)));
    }

    [Fact]
    public void Missing_column_count_defaults_to_two()
    {
        var typst = Service().RenderBlockToTypst(
            Block("columnLayout", """{"mode":"start"}""", 0));

        Assert.Equal("#set page(columns: 2)", typst);
    }

    [Fact]
    public void The_block_never_falls_through_to_the_unknown_arm()
    {
        // The regression that started all this: an unknown type returns a Typst
        // comment, which is invisible in the PDF.
        var typst = Service().RenderBlockToTypst(
            Block("columnLayout", """{"mode":"start","columns":2}""", 0));

        Assert.DoesNotContain("Unknown block type", typst);
        Assert.DoesNotContain("//", typst);
    }

    // ── the document path, which wraps instead ───────────────────────────

    // The sequence renderer is pure: blocks in, Typst out. That is deliberate —
    // this model's JsonDocument properties cannot be mapped by the in-memory EF
    // provider, so any test that reached a DbSet would fail on the model rather
    // than on the behaviour.
    private static string RenderSequence(params Block[] blocks) =>
        Service().RenderBlockSequenceToTypst(blocks);

    [Fact]
    public void Document_path_wraps_the_blocks_between_the_markers()
    {
        var typst = RenderSequence(
            Block("paragraph", """{"text":"Before the columns."}""", 0),
            Block("columnLayout", """{"mode":"start","columns":2}""", 1),
            Block("paragraph", """{"text":"Inside the columns."}""", 2),
            Block("columnLayout", """{"mode":"end"}""", 3),
            Block("paragraph", """{"text":"After the columns."}""", 4));

        // multicols semantics: a wrapper, not a page-level setting.
        Assert.Contains("#columns(2)[", typst);
        Assert.DoesNotContain("#set page(columns:", typst);

        // and the content is actually inside it
        var open = typst.IndexOf("#columns(2)[", StringComparison.Ordinal);
        var close = typst.IndexOf("]", open, StringComparison.Ordinal);
        var inside = typst[open..close];
        Assert.Contains("Inside the columns.", inside);
        Assert.DoesNotContain("Before the columns.", inside);
    }

    [Fact]
    public void Content_between_markers_is_never_dropped()
    {
        // The actual bug, stated as a test: every paragraph must survive.
        var typst = RenderSequence(
            Block("columnLayout", """{"mode":"start","columns":2}""", 0),
            Block("paragraph", """{"text":"First."}""", 1),
            Block("paragraph", """{"text":"Second."}""", 2),
            Block("columnLayout", """{"mode":"end"}""", 3));

        Assert.Contains("First.", typst);
        Assert.Contains("Second.", typst);
    }

    [Fact]
    public void An_unclosed_start_still_emits_its_content()
    {
        // A missing "end" marker is a malformed document, but it must not cost
        // the author everything after the start marker.
        var typst = RenderSequence(
            Block("columnLayout", """{"mode":"start","columns":2}""", 0),
            Block("paragraph", """{"text":"Orphaned but present."}""", 1));

        Assert.Contains("#columns(2)[", typst);
        Assert.Contains("Orphaned but present.", typst);
    }

    [Fact]
    public void A_second_start_closes_the_first()
    {
        var typst = RenderSequence(
            Block("columnLayout", """{"mode":"start","columns":2}""", 0),
            Block("paragraph", """{"text":"Two columns."}""", 1),
            Block("columnLayout", """{"mode":"start","columns":3}""", 2),
            Block("paragraph", """{"text":"Three columns."}""", 3));

        Assert.Contains("#columns(2)[", typst);
        Assert.Contains("#columns(3)[", typst);
        Assert.Contains("Two columns.", typst);
        Assert.Contains("Three columns.", typst);
    }

    [Fact]
    public void One_column_does_not_wrap()
    {
        // multicol refuses \begin{multicols}{1}; a single column is not a column
        // layout, so there is nothing to wrap.
        var typst = RenderSequence(
            Block("columnLayout", """{"mode":"start","columns":1}""", 0),
            Block("paragraph", """{"text":"Plain flow."}""", 1));

        Assert.DoesNotContain("#columns(", typst);
        Assert.Contains("Plain flow.", typst);
    }
}
