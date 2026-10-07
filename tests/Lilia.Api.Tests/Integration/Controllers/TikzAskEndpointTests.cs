using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Api.Tests.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// Ask Lilia on a figure through <c>POST /api/ai/ask</c> with <c>figure</c> (TikZ step 3, 1c), end to
/// end: who may ask what, the context the model is sent, and the real compile before a proposal
/// is shown. The model is scripted (no AI is called); TeX is real.
/// </summary>
[Collection("Integration")]
public class TikzAskEndpointTests : FourCallersTestBase, IDisposable
{
    private readonly ScriptedChatClient _chat = new();
    private readonly WebApplicationFactory<Program> _factory;

    public TikzAskEndpointTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _factory = fixture.Factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("AI:Anthropic:ApiKey", "sk-test-not-a-key");
            b.UseSetting("AI:Enabled", "true");
            b.ConfigureTestServices(s =>
            {
                var existing = s.Where(d => d.ServiceType == typeof(IChatClient)).ToList();
                foreach (var d in existing) s.Remove(d);
                s.AddSingleton<IChatClient>(_chat);
            });
        });
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Caller(string userId)
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-UserId", userId);
        c.DefaultRequestHeaders.Add("X-Test-Email", $"{userId}@lilia.test");
        c.DefaultRequestHeaders.Add("X-Test-Name", userId);
        return c;
    }

    // Unique per run, so the drawing cache cannot answer from an earlier run.
    private static string Picture(string body = @"\draw (0,0) -- (1,1);") =>
        $"\\begin{{tikzpicture}} % {Guid.NewGuid():N}\n  {body}\n\\end{{tikzpicture}}";

    private async Task<(Seeded S, Guid Figure, Guid Intro)> SeedAsync(string source)
    {
        var s = await SeedSharedDocumentAsync();   // "Private text" paragraph at 0
        await SeedBlockAsync(s.DocumentId, "heading", """{"text":"Pipelines","level":1}""", 1);
        var intro = await SeedBlockAsync(s.DocumentId, "paragraph", """{"text":"The pipeline has three stages."}""", 2);
        var fig = await SeedBlockAsync(s.DocumentId, "figure",
            JsonSerializer.Serialize(new { kind = "tikz", source, caption = "The pipeline.", label = "fig:p" }), 3);
        return (s, fig.Id, intro.Id);
    }

    private static object Ask(Guid doc, string message, object figure) => new { message, documentId = doc.ToString(), figure };

    [Fact]
    public async Task A_stranger_is_refused_and_the_model_is_not_called()
    {
        var (s, fig, _) = await SeedAsync(Picture());
        using var c = Caller(StrangerId);

        var res = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "Explain this figure", new { blockId = fig, intent = "explain" }));

        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await res.Content.ReadAsStringAsync()).Should().Contain("no-access");
        _chat.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_viewer_may_have_it_explained_but_not_changed()
    {
        var (s, fig, _) = await SeedAsync(Picture());
        using var c = Caller(ViewerId);

        var change = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "make it red", new { blockId = fig, intent = "change" }));
        change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await change.Content.ReadAsStringAsync()).Should().Contain("read-only");
        _chat.Calls.Should().BeEmpty();

        _chat.Enqueue("A line from the origin.\n[L2] The line from (0,0) to (1,1).");
        var explain = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "Explain this figure", new { blockId = fig, intent = "explain" }));
        explain.StatusCode.Should().Be(HttpStatusCode.OK, await explain.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await explain.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("reply").GetString().Should().Be("A line from the origin.");
        var f = body.GetProperty("figure");
        f.GetProperty("kind").GetString().Should().Be("explain");
        f.GetProperty("lines")[0].GetProperty("from").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task An_editors_draw_is_compiled_and_comes_back_drawn_with_only_the_figures_context_sent()
    {
        var (s, _, intro) = await SeedAsync(Picture());
        using var c = Caller(EditorId);
        var drawn = Picture(@"\node[draw] (a) {Read}; \node[draw, right of=a, node distance=2cm] (b) {Parse}; \draw[->] (a) -- (b);");
        _chat.Enqueue($"Here's the pipeline. It drew.\n```tikz\n{drawn}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "draw the pipeline described above", new { afterBlockId = intro, intent = "draw" }));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var f = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("figure");
        f.GetProperty("kind").GetString().Should().Be("draw");
        f.GetProperty("svg").GetString().Should().Contain("<svg");
        f.GetProperty("attempts").GetInt32().Should().Be(1);
        f.GetProperty("source").GetString().Should().Be(drawn);

        var system = _chat.Calls.Single().First().Text;
        system.Should().Contain("The pipeline has three stages.").And.Contain("Source: (empty)");
        system.Should().NotContain("Private text", "not the whole document").And.NotContain("Authz paper");
    }

    [Fact]
    public async Task A_change_that_does_not_draw_is_retried_with_TeXs_error_and_two_failures_name_the_line()
    {
        var saved = Picture();
        var (s, fig, _) = await SeedAsync(saved);
        using var c = Caller(OwnerId);
        var broken = saved.Replace(@"\draw (0,0) -- (1,1);", @"\draw (0,0) -- (1,1); \nosuchcommand");
        var good = saved.Replace(@"\draw (0,0)", @"\draw[dashed] (0,0)");
        _chat.Enqueue($"Dashed.\n```tikz\n{broken}\n```", $"Fixed: dashed.\n```tikz\n{good}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "make it dashed", new { blockId = fig, intent = "change" }));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var f = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("figure");
        f.GetProperty("kind").GetString().Should().Be("change");
        f.GetProperty("attempts").GetInt32().Should().Be(2);
        f.GetProperty("source").GetString().Should().Be(good);
        _chat.Calls[1].Last().Text.Should().Contain("That did not draw").And.Contain("line 2").And.Contain(@"\nosuchcommand");

        _chat.Enqueue($"Dashed.\n```tikz\n{broken}\n```", $"Again.\n```tikz\n{broken}\n```");
        var twice = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId, "make it dashed", new { blockId = fig, intent = "change" }));
        var body = JsonDocument.Parse(await twice.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("reply").GetString().Should().Be("I couldn't get this to draw. Here's the source; the error is on line 2.");
        body.GetProperty("figure").GetProperty("kind").GetString().Should().Be("failed");
        body.GetProperty("figure").TryGetProperty("svg", out _).Should().BeFalse("only drawings that drew are shown");
    }

    [Fact]
    public async Task A_draw_that_uses_right_of_without_listing_positioning_draws_first_time()
    {
        var (s, _, intro) = await SeedAsync(Picture());
        using var c = Caller(EditorId);
        var square = Picture(@"\node (A) {$A$}; \node (B) [right=of A] {$B$}; \node (C) [below=of A] {$C$}; \node (D) [right=of C] {$D$};
  \draw[->] (A) -- (B); \draw[->] (A) -- node[left] {$f$} (C); \draw[->] (B) -- node[right] {$g$} (D); \draw[->] (C) -- (D);");
        _chat.Enqueue($"Here's the square.\n```tikz\n{square}\n```");

        var res = await c.PostAsJsonAsync("/api/ai/ask", Ask(s.DocumentId,
            "Draw a commutative square: A to B on top, C to D below, vertical maps f and g.", new { afterBlockId = intro, intent = "draw" }));

        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var f = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("figure");
        f.GetProperty("kind").GetString().Should().Be("draw", f.ToString());
        f.GetProperty("attempts").GetInt32().Should().Be(1);
        f.GetProperty("preambleAdditions")[0].GetString().Should().Be(@"\usetikzlibrary{positioning}");
        _chat.Calls.Should().HaveCount(1, "it drew without a retry");
    }
}
