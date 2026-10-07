using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Services;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// <c>GET /api/documents/{documentId}/blocks/{blockId}/figure.svg</c> (TikZ figures, step 1):
/// the drawing, cached; 422 <c>{ kind, message, line, excerpt, column, hasLastGood }</c> when it
/// does not draw; <c>?lastGood=true</c>; read access like every document route; the guard before
/// any compile; and the figure listed as an issue on its block.
/// </summary>
[Collection("Integration")]
public class TikzFigureEndpointTests : FourCallersTestBase
{
    public TikzFigureEndpointTests(TestDatabaseFixture fixture) : base(fixture) { }

    // Unique per test run, so the content cache cannot answer from an earlier run.
    private static string Picture(string body = @"\draw (0,0) -- (1,1);") =>
        $"\\begin{{tikzpicture}} % {Guid.NewGuid():N}\n  {body}\n\\end{{tikzpicture}}";

    private async Task<Guid> SeedFigureAsync(Guid documentId, string source, int sort = 1) =>
        (await SeedBlockAsync(documentId, "figure",
            JsonSerializer.Serialize(new { kind = "tikz", source, caption = "A figure.", label = "fig:a" }), sort)).Id;

    private static string Route(Guid doc, Guid block, string query = "") => $"/api/documents/{doc}/blocks/{block}/figure.svg{query}";

    [Fact]
    public async Task It_returns_the_svg_and_the_second_call_comes_from_the_cache()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture());
        using var c = As(ViewerId);

        var first = await c.GetAsync(Route(s.DocumentId, block));
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        first.Content.Headers.ContentType!.MediaType.Should().Be("image/svg+xml");
        (await first.Content.ReadAsStringAsync()).Should().Contain("<svg");
        first.Headers.GetValues("X-Lilia-Cache").Single().Should().Be("miss");

        var second = await c.GetAsync(Route(s.DocumentId, block));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        second.Headers.GetValues("X-Lilia-Cache").Single().Should().Be("hit");

        // The editor's cache-buster is ignored.
        (await c.GetAsync(Route(s.DocumentId, block, "?h=abc123"))).StatusCode.Should().Be(HttpStatusCode.OK);

        var last = await c.GetAsync(Route(s.DocumentId, block, "?lastGood=true"));
        last.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_compile_error_is_422_with_the_line_in_the_figures_source()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture(@"\draw (0,0) -- \undefinedthing (1,1);"));
        using var c = As(OwnerId);

        (await c.GetAsync(Route(s.DocumentId, block, "?lastGood=true"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "it never drew");

        var res = await c.GetAsync(Route(s.DocumentId, block));
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("kind").GetString().Should().Be("tex");
        body.GetProperty("line").GetInt32().Should().Be(2);
        body.GetProperty("message").GetString().Should().Contain(@"\undefinedthing");
        body.GetProperty("excerpt").GetString().Should().Be(@"  \draw (0,0) -- \undefinedthing (1,1);");
        body.GetProperty("column").GetProperty("start").GetInt32().Should().Be(17, "0-based");
        body.GetProperty("column").GetProperty("end").GetInt32().Should().Be(32, "exclusive: \\undefinedthing");
        body.GetProperty("hasLastGood").GetBoolean().Should().BeFalse();

        // Listed among the document's issues, on that block.
        var issues = await c.GetFromJsonAsync<JsonElement>($"/api/latex/{s.DocumentId}/validation-errors");
        issues.EnumerateArray().Should().Contain(i =>
            i.GetProperty("blockId").GetGuid() == block && i.GetProperty("status").GetString() == "error"
            && i.GetProperty("errorMessage").GetString()!.Contains("TikZ figure doesn't draw (line 2)"));
    }

    [Fact]
    public async Task The_guard_refuses_an_unsafe_picture_with_a_422_before_any_compile()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture(@"\node {\input{/etc/hostname}};"));
        using var c = As(OwnerId);
        var res = await c.GetAsync(Route(s.DocumentId, block));
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("line").GetInt32().Should().Be(2);
        body.GetProperty("message").GetString().Should().Contain("server");
    }

    [Fact]
    public async Task Access_is_401_without_a_login_403_for_a_stranger_404_for_an_unknown_or_foreign_block()
    {
        var s = await SeedSharedDocumentAsync();
        var block = await SeedFigureAsync(s.DocumentId, Picture());

        using (var anon = CreateAnonymousClient())
            (await anon.GetAsync(Route(s.DocumentId, block))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var stranger = As(StrangerId))
            (await stranger.GetAsync(Route(s.DocumentId, block))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var owner = As(OwnerId);
        (await owner.GetAsync(Route(s.DocumentId, Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync(Route(s.DocumentId, s.BlockId))).StatusCode.Should().Be(HttpStatusCode.NotFound, "a paragraph is not a TikZ figure");

        // A block of another document, named under a document the caller can read.
        var other = await SeedDocumentAsync(OwnerId, "Other");
        var foreign = await SeedFigureAsync(other.Id, Picture());
        (await owner.GetAsync(Route(s.DocumentId, foreign))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(TikzFigureTests.CorpusFiles), MemberType = typeof(TikzFigureTests))]
    public async Task Each_corpus_figure_draws_through_the_endpoint(string file)
    {
        var tex = await File.ReadAllTextAsync(Path.Combine(TikzFigureTests.CorpusDir, file));
        var imported = await TikzFigureTests.ImportAsync(tex);
        await SeedUserAsync(OwnerId);
        var doc = await SeedDocumentAsync(OwnerId, file);
        Guid figure = default;
        await using (var db = CreateDbContext())
        {
            foreach (var b in imported.Blocks)
            {
                b.Id = Guid.NewGuid();
                b.DocumentId = doc.Id;
                b.CreatedAt = b.UpdatedAt = DateTime.UtcNow;
                db.Blocks.Add(b);
                if (b.Type == "figure") figure = b.Id;
            }
            await db.SaveChangesAsync();
        }

        using var c = As(OwnerId);
        var res = await c.GetAsync(Route(doc.Id, figure));
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var svg = await res.Content.ReadAsStringAsync();
        svg.Should().StartWith("<?xml").And.Contain("<svg").And.Contain("</svg>");

        // The preview keeps the fast path and places the drawing with its caption.
        var pdf = await c.PostAsync($"/api/latex/{doc.Id}/pdf", null);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Headers.GetValues("X-Render-Engine").Single().Should().Be("typst");
        Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(), 0, 4).Should().Be("%PDF");
    }
}
