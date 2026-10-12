using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// The Lilia MCP server at /api/mcp, step 1 (read-only tools; the user, 12 Oct: "Yes go ahead for
/// now"). JSON-RPC over Streamable HTTP, signed in like the rest of /api, and every document tool
/// limited to what the signed-in author may open.
/// </summary>
[Collection("Integration")]
public class LiliaMcpServerTests : IntegrationTestBase
{
    public LiliaMcpServerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static readonly string[] ReadTools =
    [
        "list_documents", "get_outline", "get_block", "search_document", "get_lilia_latex",
        "get_document_settings", "check_document", "search_help", "get_help",
    ];

    /// <summary>One JSON-RPC call; the reply may come as JSON or as one SSE "data:" line.</summary>
    private static async Task<(HttpStatusCode Status, JsonElement Body)> RpcAsync(HttpClient client, string method, object? @params = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = @params ?? new { } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) return (res.StatusCode, default);
        var json = text.TrimStart().StartsWith('{')
            ? text
            : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        return (res.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var (status, body) = await RpcAsync(client, "tools/call", new { name = tool, arguments = args });
        status.Should().Be(HttpStatusCode.OK);
        var text = body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Without_a_sign_in_it_answers_401()
    {
        var (status, _) = await RpcAsync(CreateAnonymousClient(), "tools/list");
        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task It_lists_the_tools_and_the_read_ones_say_so()
    {
        await SeedUserAsync("mcp_lister");
        var (status, body) = await RpcAsync(CreateClientAs("mcp_lister"), "tools/list");
        status.Should().Be(HttpStatusCode.OK);
        var tools = body.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        var read = tools.Where(t => ReadTools.Contains(t.GetProperty("name").GetString())).ToList();
        read.Should().HaveCount(ReadTools.Length);
        read.Should().OnlyContain(t => t.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        // Step 3's edit tools are listed too, and say they write.
        tools.Select(t => t.GetProperty("name").GetString()).Should().Contain(new[] { "add_block", "edit_block", "remove_block", "apply_lml" });
        foreach (var t in tools.Where(t => !ReadTools.Contains(t.GetProperty("name").GetString())))
            (t.GetProperty("annotations").TryGetProperty("readOnlyHint", out var ro) && ro.GetBoolean()).Should().BeFalse();
    }

    [Fact]
    public async Task An_author_reads_their_own_document()
    {
        await SeedUserAsync("mcp_owner");
        var doc = await SeedDocumentAsync("mcp_owner", "Notes on tides");
        var heading = await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The moon pulls the sea."}""", 1);
        var client = CreateClientAs("mcp_owner");

        var list = await CallAsync(client, "list_documents", new { });
        list.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().Contain(doc.Id);

        var outline = await CallAsync(client, "get_outline", new { documentId = doc.Id.ToString() });
        outline.GetProperty("blockCount").GetInt32().Should().Be(2);
        outline.GetProperty("outline")[0].GetProperty("text").GetString().Should().Be("Introduction");

        var found = await CallAsync(client, "search_document", new { documentId = doc.Id.ToString(), query = "moon" });
        found.GetProperty("matches").GetArrayLength().Should().Be(1);

        var block = await CallAsync(client, "get_block", new { documentId = doc.Id.ToString(), blockId = heading.Id.ToString() });
        block.GetProperty("type").GetString().Should().Be("heading");
    }

    [Fact]
    public async Task Someone_elses_document_is_not_there()
    {
        await SeedUserAsync("mcp_alice");
        await SeedUserAsync("mcp_mallory");
        var doc = await SeedDocumentAsync("mcp_alice", "Private draft");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"secret"}""", 0);
        var mallory = CreateClientAs("mcp_mallory");

        var list = await CallAsync(mallory, "list_documents", new { });
        list.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).Should().NotContain(doc.Id);

        foreach (var tool in new[] { "get_outline", "get_document_settings", "get_lilia_latex", "check_document" })
        {
            var r = await CallAsync(mallory, tool, new { documentId = doc.Id.ToString() });
            r.GetProperty("error").GetString().Should().Contain("No document", $"{tool} must not open it");
        }
    }

    [Fact]
    public async Task Links_use_the_public_address()
    {
        // The tools built links from Email:BaseUrl, the machine's address on production (12 Oct).
        await SeedUserAsync("mcp_links");
        var doc = await SeedDocumentAsync("mcp_links", "Linked");
        var list = await CallAsync(CreateClientAs("mcp_links"), "list_documents", new { });
        var url = list.GetProperty("documents").EnumerateArray().First(d => d.GetProperty("id").GetGuid() == doc.Id)
            .GetProperty("url").GetString();
        url.Should().Be($"https://editor.liliaeditor.com/d/{doc.Id}");
    }

    [Fact]
    public async Task A_help_article_comes_with_full_picture_addresses()
    {
        using (var scope = Fixture.Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IKbSeeder>().PreloadAsync();
        await SeedUserAsync("mcp_help");
        var r = await CallAsync(CreateClientAs("mcp_help"), "get_help", new { slug = "sharing", lang = "fr" });
        r.GetProperty("url").GetString().Should().Be("https://editor.liliaeditor.com/help/sharing");
        var steps = r.GetProperty("article").GetProperty("steps").GetString();
        steps.Should().Contain("](https://editor.liliaeditor.com/help/img/sharing/fr-1.png)").And.NotContain("](/help");
    }
}
