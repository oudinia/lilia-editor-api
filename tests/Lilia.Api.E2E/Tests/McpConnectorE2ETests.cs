using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests;

/// <summary>
/// The connector (/api/mcp) against a running API: the write tools' reorder and undo point, the
/// LaTeX it refuses, the signed download link, the OAuth metadata apps read first, and the per-user
/// rate limit. JSON-RPC over HTTP, as an app sends it. Run against the test copy (local Postgres),
/// never production: E2E__ApiBaseUrl=http://localhost:5041.
/// </summary>
public class McpConnectorE2ETests : E2ETestBase
{
    private static HttpRequestMessage Rpc(string method, object? @params = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = @params ?? new { } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return req;
    }

    /// <summary>One tool call; the tool's own JSON result (the reply may come as JSON or one SSE "data:" line).</summary>
    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var res = await client.SendAsync(Rpc("tools/call", new { name = tool, arguments = args }));
        var text = await res.Content.ReadAsStringAsync();
        res.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private async Task<(HttpClient Client, string DocId, List<string> BlockIds)> PaperAsync(string title, params string[] paragraphs)
    {
        var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, title);
        var id = doc.GetProperty("id").GetString()!;
        var ids = new List<string>();
        foreach (var p in paragraphs)
            ids.Add((await CreateTestBlockAsync(client, id, "paragraph", new { text = p })).GetProperty("id").GetString()!);
        return (client, id, ids);
    }

    private static async Task<List<(string Id, string Text)>> BlocksAsync(HttpClient client, string docId)
    {
        var doc = await client.GetFromJsonAsync<JsonElement>($"/api/documents/{docId}");
        return doc.GetProperty("blocks").EnumerateArray()
            .OrderBy(b => b.GetProperty("sortOrder").GetInt32())
            .Select(b => (b.GetProperty("id").GetString()!,
                b.GetProperty("content").TryGetProperty("text", out var t) ? t.GetString() ?? "" : ""))
            .ToList();
    }

    [Fact]
    public async Task Reorder_blocks_puts_them_in_the_order_given()
    {
        var (client, docId, _) = await PaperAsync("MCP reorder", "One.", "Two.", "Three.");
        // Every block, the new document's empty first one included.
        var reversed = (await BlocksAsync(client, docId)).Select(b => b.Id).Reverse().ToArray();

        var r = await CallAsync(client, "reorder_blocks", new { documentId = docId, blockIds = reversed });
        r.TryGetProperty("error", out var err).Should().BeFalse(err.ToString());

        (await BlocksAsync(client, docId)).Select(b => b.Id).Should().Equal(reversed);
        (await BlocksAsync(client, docId)).Select(b => b.Text).Where(t => t != "").Should().Equal("Three.", "Two.", "One.");
    }

    [Fact]
    public async Task An_edit_saves_an_undo_point_first_and_restoring_it_puts_the_text_back()
    {
        var (client, docId, ids) = await PaperAsync("MCP undo", "As the author wrote it.");

        var r = await CallAsync(client, "edit_block", new { documentId = docId, blockId = ids[0], content = """{"text":"As the app rewrote it."}""" });
        r.TryGetProperty("error", out var err).Should().BeFalse(err.ToString());
        var undo = r.GetProperty("undo").GetProperty("versionId").GetString()!;
        (await BlocksAsync(client, docId)).Single(b => b.Id == ids[0]).Text.Should().Be("As the app rewrote it.");

        // A second change in the same burst keeps the same undo point.
        var r2 = await CallAsync(client, "set_title", new { documentId = docId, title = "MCP undo, retitled" });
        r2.GetProperty("undo").GetProperty("versionId").GetString().Should().Be(undo);

        var restore = await client.PostAsync($"/api/documents/{docId}/versions/{undo}/restore", null);
        restore.StatusCode.Should().Be(HttpStatusCode.OK, await restore.Content.ReadAsStringAsync());
        (await BlocksAsync(client, docId)).Should().Contain(b => b.Text == "As the author wrote it.").And.NotContain(b => b.Text == "As the app rewrote it.");
    }

    [Theory]
    [InlineData("""{"text":"x \\immediate\\write18{rm -rf /}"}""")]
    [InlineData("""{"text":"\\input{/etc/passwd}"}""")]
    public async Task Unsafe_latex_is_refused_and_nothing_is_stored(string content)
    {
        var (client, docId, _) = await PaperAsync("MCP refused", "Already here.");
        var before = (await BlocksAsync(client, docId)).Count;
        var r = await CallAsync(client, "add_block", new { documentId = docId, type = "paragraph", content });
        r.GetProperty("category").GetString().Should().Be("refused");
        r.GetProperty("error").GetString().Should().StartWith("Lilia refuses this LaTeX");
        (await BlocksAsync(client, docId)).Should().HaveCount(before);
    }

    [Fact]
    public async Task Export_gives_a_signed_link_that_works_without_signing_in_and_a_made_up_one_does_not()
    {
        var (client, docId, _) = await PaperAsync("MCP export", "Exported through a link.");
        var r = await CallAsync(client, "export_document", new { documentId = docId, format = "tex" });
        var url = r.GetProperty("url").GetString()!;
        url.Should().Contain("/api/connector/files/");

        // The link is the credential: no sign-in header at all.
        using var anyone = CreateClient();
        var res = await anyone.GetAsync(new Uri(url).PathAndQuery);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Exported through a link.").And.Contain("\\documentclass");

        var made = await anyone.GetAsync($"/api/connector/files/{new string('a', 48)}");
        made.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_protected_resource_metadata_names_the_endpoint_and_its_authorisation_server()
    {
        using var anyone = CreateClient();
        var meta = await anyone.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/api/mcp");
        meta.GetProperty("resource").GetString().Should().EndWith("/api/mcp");
        meta.TryGetProperty("authorization_servers", out var servers).Should().BeTrue(meta.ToString());
        servers.ValueKind.Should().Be(JsonValueKind.Array);
        meta.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()).Should().Contain("openid");
    }

    [Fact]
    public async Task The_connector_is_rate_limited_per_user()
    {
        // Two people of their own, so no other test's budget is spent.
        HttpClient As(string id)
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add(AuthTokenProvider.DevUserHeader, id);
            return c;
        }
        var stamp = Guid.NewGuid().ToString("N")[..10];
        using var busy = As($"e2e_mcp_busy_{stamp}");
        using var calm = As($"e2e_mcp_calm_{stamp}");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 61; i++)
            statuses.Add((await busy.SendAsync(Rpc("tools/list"))).StatusCode);
        statuses.Take(60).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
        (await calm.SendAsync(Rpc("tools/list"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
