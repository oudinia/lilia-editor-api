using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// TikZ figures, step 3 (the split view and theme colours):
/// <c>POST …/blocks/{blockId}/figure/draft</c> <c>{ source }</c> (write access) draws unsaved source
/// with figure.svg's answers and cache, never touching the last good drawing, with
/// <c>X-Draw-Ms</c>; <c>GET …/figure/colours</c> resolves the theme colour names for the figure
/// (Index: its chapter); <c>GET /api/themes</c> carries each theme's <c>tikzColours</c>.
/// </summary>
[Collection("Integration")]
public class TikzDraftEndpointTests : FourCallersTestBase
{
    public TikzDraftEndpointTests(TestDatabaseFixture fixture) : base(fixture) { }

    // Unique per test run, so the content cache cannot answer from an earlier run.
    private static string Picture(string body = @"\draw (0,0) -- (1,1);") =>
        $"\\begin{{tikzpicture}} % {Guid.NewGuid():N}\n  {body}\n\\end{{tikzpicture}}";

    private async Task<Guid> SeedFigureAsync(Guid documentId, string source, int sort = 1) =>
        (await SeedBlockAsync(documentId, "figure",
            JsonSerializer.Serialize(new { kind = "tikz", source, caption = "A figure.", label = "fig:a" }), sort)).Id;

    private static string Draft(Guid doc, Guid block) => $"/api/documents/{doc}/blocks/{block}/figure/draft";
    private static string Svg(Guid doc, Guid block, string query = "") => $"/api/documents/{doc}/blocks/{block}/figure.svg{query}";
    private static string Colours(Guid doc, Guid block) => $"/api/documents/{doc}/blocks/{block}/figure/colours";

    [Fact]
    public async Task A_draft_draws_with_the_time_it_took_and_the_same_draft_comes_from_the_cache()
    {
        var s = await SeedSharedDocumentAsync();
        var saved = Picture();
        var block = await SeedFigureAsync(s.DocumentId, saved);
        using var c = As(EditorId);

        var draft = Picture(@"\draw (0,0) -- (2,1);");
        var first = await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = draft });
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        (await first.Content.ReadAsStringAsync()).Should().Contain("<svg");
        first.Headers.GetValues("X-Lilia-Cache").Single().Should().Be("miss");
        int.Parse(first.Headers.GetValues("X-Draw-Ms").Single()).Should().BeGreaterThan(0);

        var again = await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = draft });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        again.Headers.GetValues("X-Lilia-Cache").Single().Should().Be("hit");

        // The saved figure, drawn once, is a hit as a draft too.
        (await c.GetAsync(Svg(s.DocumentId, block))).StatusCode.Should().Be(HttpStatusCode.OK);
        var savedAsDraft = await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = saved });
        savedAsDraft.Headers.GetValues("X-Lilia-Cache").Single().Should().Be("hit");
    }

    [Fact]
    public async Task A_draft_that_does_not_draw_is_422_with_the_line_and_leaves_the_last_good_drawing()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture());
        using var c = As(OwnerId);

        var drawn = await c.GetAsync(Svg(s.DocumentId, block));
        drawn.StatusCode.Should().Be(HttpStatusCode.OK);
        var lastGood = await (await c.GetAsync(Svg(s.DocumentId, block, "?lastGood=true"))).Content.ReadAsByteArrayAsync();

        // A good draft, different from the saved one: the last good drawing stays the saved one.
        (await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = Picture(@"\draw (0,0) circle (1);") }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await c.GetAsync(Svg(s.DocumentId, block, "?lastGood=true"))).Content.ReadAsByteArrayAsync())
            .Should().Equal(lastGood, "a draft never updates the last good drawing");

        var res = await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = Picture(@"\draw (0,0) -- \undefinedthing (1,1);") });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        res.Headers.Contains("X-Draw-Ms").Should().BeTrue();
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("kind").GetString().Should().Be("tex");
        body.GetProperty("line").GetInt32().Should().Be(2);
        body.GetProperty("message").GetString().Should().Contain(@"\undefinedthing");
        body.GetProperty("excerpt").GetString().Should().Be(@"  \draw (0,0) -- \undefinedthing (1,1);");
        body.GetProperty("column").GetProperty("start").GetInt32().Should().Be(17);
        body.GetProperty("hasLastGood").GetBoolean().Should().BeTrue();

        // The saved figure is still fine: a draft records no issue on the block.
        var issues = await c.GetFromJsonAsync<JsonElement>($"/api/latex/{s.DocumentId}/validation-errors");
        issues.EnumerateArray().Should().NotContain(i => i.GetProperty("blockId").GetGuid() == block && i.GetProperty("status").GetString() == "error");
    }

    [Fact]
    public async Task A_table_draws_the_plot_that_will_follow_it_before_the_figure_exists()
    {
        // Plot this table (step 4a): the dialog previews the plot on the table's own block id.
        var s = await SeedSharedDocumentAsync();
        var table = (await SeedBlockAsync(s.DocumentId, "table",
            JsonSerializer.Serialize(new { headers = new[] { "Epoch", "Loss" }, rows = new[] { new[] { "1", "0.9" }, new[] { "2", "0.5" } } }), 1)).Id;
        using var c = As(EditorId);

        var plot = $"% {Guid.NewGuid():N}\n\\begin{{tikzpicture}}\\begin{{axis}}[width=6cm]\\addplot[color=lilia-seq1, mark=*] coordinates {{(1,0.9) (2,0.5)}};\\end{{axis}}\\end{{tikzpicture}}";
        var res = await c.PostAsJsonAsync(Draft(s.DocumentId, table), new { source = plot });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        (await res.Content.ReadAsStringAsync()).Should().Contain("<svg");

        // Only the draft: a table has no figure.svg, and a paragraph still has no draft.
        (await c.GetAsync(Svg(s.DocumentId, table))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.PostAsJsonAsync(Draft(s.DocumentId, s.BlockId), new { source = plot })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Drafts_need_write_access_401_403_404_and_a_source()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture());
        var body = new { source = Picture() };

        using (var anon = CreateAnonymousClient())
            (await anon.PostAsJsonAsync(Draft(s.DocumentId, block), body)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var stranger = As(StrangerId))
            (await stranger.PostAsJsonAsync(Draft(s.DocumentId, block), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var viewer = As(ViewerId))
            (await viewer.PostAsJsonAsync(Draft(s.DocumentId, block), body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a draft is editing");

        using var owner = As(OwnerId);
        (await owner.PostAsJsonAsync(Draft(s.DocumentId, Guid.NewGuid()), body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.PostAsJsonAsync(Draft(s.DocumentId, s.BlockId), body)).StatusCode.Should().Be(HttpStatusCode.NotFound, "a paragraph is not a TikZ figure");
        var other = await SeedDocumentAsync(OwnerId, "Other");
        var foreign = await SeedFigureAsync(other.Id, Picture());
        (await owner.PostAsJsonAsync(Draft(s.DocumentId, foreign), body)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await owner.PostAsJsonAsync(Draft(s.DocumentId, block), new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await owner.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = new string('x', 200_001) }))
            .StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var empty = await owner.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = "  " });
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task The_guard_refuses_an_unsafe_draft_before_any_compile()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture());
        using var c = As(OwnerId);
        var res = await c.PostAsJsonAsync(Draft(s.DocumentId, block), new { source = Picture(@"\node {\input{/etc/hostname}};") });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("line").GetInt32().Should().Be(2);
        body.GetProperty("message").GetString().Should().Contain("server");
    }

    /// <summary>Whether the SVG paints this colour (pdftocairo writes rgb(…%, …%, …%)).</summary>
    private static bool HasColour(string svg, string hex) =>
        System.Text.RegularExpressions.Regex.Matches(svg, @"rgb\(\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)")
            .Any(m => Enumerable.Range(0, 3).All(i =>
                Math.Abs(double.Parse(m.Groups[i + 1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0
                         - Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16) / 255.0) < 0.006));

    private async Task SetLookAsync(Guid documentId, object look)
    {
        await using var db = CreateDbContext();
        var doc = await db.Documents.FindAsync(documentId);
        doc!.Look = JsonSerializer.Serialize(look);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_colours_follow_the_figures_Index_chapter_and_the_drawing_follows_them()
    {
        var s = await SeedSharedDocumentAsync();
        var one = await SeedBlockAsync(s.DocumentId, "heading", JsonSerializer.Serialize(new { text = "One", level = 1 }), 10);
        var source = Picture(@"\fill[lilia-chapter] (0,0) rectangle (2,1);");
        var first = await SeedFigureAsync(s.DocumentId, source, 11);
        var two = await SeedBlockAsync(s.DocumentId, "heading", JsonSerializer.Serialize(new { text = "Two", level = 1 }), 20);
        var second = await SeedFigureAsync(s.DocumentId, source, 21);
        using var c = As(ViewerId);

        // Classic: ink and greys, the chapter is the accent.
        var classic = await c.GetFromJsonAsync<Dictionary<string, string>>(Colours(s.DocumentId, first));
        classic!.Keys.Should().HaveCount(13).And.Contain(["lilia-ink", "lilia-paper", "lilia-accent", "lilia-accent-soft", "lilia-chapter", "lilia-seq1", "lilia-seq8"]);
        classic["lilia-chapter"].Should().Be("#1A1A1A");

        // Index, chapter two pinned to the eighth colour.
        await SetLookAsync(s.DocumentId, new { theme = "index", paper = "theme", pins = new Dictionary<string, int> { [two.Id.ToString()] = 7 } });
        var a = await c.GetFromJsonAsync<Dictionary<string, string>>(Colours(s.DocumentId, first));
        var b = await c.GetFromJsonAsync<Dictionary<string, string>>(Colours(s.DocumentId, second));
        a!["lilia-chapter"].Should().Be("#4A5FA3");
        b!["lilia-chapter"].Should().Be("#2E6E9E");
        a["lilia-accent"].Should().Be("#4A5FA3");
        a["lilia-ink"].Should().Be("#2F2E2C");

        // The same source, two drawings: each in its chapter's colour.
        var svgA = await (await c.GetAsync(Svg(s.DocumentId, first))).Content.ReadAsStringAsync();
        var svgB = await (await c.GetAsync(Svg(s.DocumentId, second))).Content.ReadAsStringAsync();
        HasColour(svgA, "#4A5FA3").Should().BeTrue();
        HasColour(svgA, "#2E6E9E").Should().BeFalse();
        HasColour(svgB, "#2E6E9E").Should().BeTrue();

        // Moving the second figure under chapter one recolours it on the next draw.
        await using (var db = CreateDbContext())
        {
            var blk = await db.Blocks.FindAsync(second);
            blk!.SortOrder = 12;
            await db.SaveChangesAsync();
        }
        (await c.GetFromJsonAsync<Dictionary<string, string>>(Colours(s.DocumentId, second)))!["lilia-chapter"].Should().Be("#4A5FA3");
        HasColour(await (await c.GetAsync(Svg(s.DocumentId, second))).Content.ReadAsStringAsync(), "#4A5FA3").Should().BeTrue();

        using (var stranger = As(StrangerId))
            (await stranger.GetAsync(Colours(s.DocumentId, first))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.GetAsync(Colours(s.DocumentId, s.BlockId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_theme_list_carries_each_themes_colour_names()
    {
        using var c = As(OwnerId);
        var themes = JsonDocument.Parse(await c.GetStringAsync("/api/themes")).RootElement;
        foreach (var t in themes.EnumerateArray())
        {
            var colours = t.GetProperty("tikzColours");
            colours.EnumerateObject().Select(p => p.Name).Should().HaveCount(13);
            colours.GetProperty("lilia-chapter").GetString().Should().Be(colours.GetProperty("lilia-accent").GetString());
        }
        var cerulean = themes.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "cerulean").GetProperty("tikzColours");
        cerulean.GetProperty("lilia-accent").GetString().Should().Be("#0A76A4");
        cerulean.GetProperty("lilia-seq3").GetString().Should().Be("#B8303A");
        var classic = themes.EnumerateArray().Single(t => t.GetProperty("id").GetString() == "classic").GetProperty("tikzColours");
        classic.GetProperty("lilia-seq3").GetString().Should().Be("#999999", "Classic's sequence is greys");
    }
}
