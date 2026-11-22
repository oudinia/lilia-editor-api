using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The Lilia MCP server at /api/mcp, step 1 (read-only tools; the user, 12 Oct: "Yes go ahead for
/// now"). JSON-RPC over Streamable HTTP, signed in like the rest of /api, and every document tool
/// limited to what the signed-in author may open.
/// </summary>
public class LiliaMcpServerTests : IntegrationTestBase
{
    public LiliaMcpServerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static readonly string[] ReadTools =
    [
        "list_documents", "get_outline", "get_document", "get_block", "search_document", "get_latex",
        "get_document_settings", "check_document", "search_help", "get_help",
        // Read-only too: a download link, and where an import is.
        "export_document", "import_status",
        // The connector test, 17 Nov 2026 (items D and G): the saved versions and the comments.
        "list_versions", "list_comments",
        // 17 Nov 2026 (the connector test); add_section, 22 Nov 2026 (launch-bot #276); start_bulk_import and
        // bulk_import_status, 22 Nov 2026 (the owner: bulk import through the connector too); whoami, 22 Nov 2026 (launch-bot #269).
        tools.Should().HaveCount(36);
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
        tools.Select(t => t.GetProperty("name").GetString()).Should().Contain(new[] { "add_block", "add_section", "edit_block", "remove_block", "rewrite_document", "get_latex" })
            .And.NotContain(new[] { "apply_lml", "get_lilia_latex" });
        foreach (var t in tools.Where(t => !ReadTools.Contains(t.GetProperty("name").GetString())))
            (t.GetProperty("annotations").TryGetProperty("readOnlyHint", out var ro) && ro.GetBoolean()).Should().BeFalse();
    }

    [Fact]
    public async Task Every_tool_has_a_title_and_says_whether_it_reads_or_changes()
    {
        // Anthropic's Connectors Directory requires both on every tool (5 Nov 2026).
        await SeedUserAsync("mcp_titles");
        var (_, body) = await RpcAsync(CreateClientAs("mcp_titles"), "tools/list");
        var tools = body.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        // get_document, add_reference, add_figure_image, then list_versions, restore_version and the four comment tools,
        // 17 Nov 2026 (the connector test); add_section, 22 Nov 2026 (launch-bot #276); start_bulk_import and
        // bulk_import_status, 22 Nov 2026 (the owner: bulk import through the connector too); whoami, 22 Nov 2026 (launch-bot #269).
        tools.Should().HaveCount(36);
        foreach (var t in tools)
        {
            var name = t.GetProperty("name").GetString();
            t.TryGetProperty("title", out var title).Should().BeTrue(name);
            title.GetString().Should().NotBeNullOrWhiteSpace(name);
            var a = t.GetProperty("annotations");
            a.GetProperty("title").GetString().Should().Be(title.GetString(), name);
            (a.TryGetProperty("readOnlyHint", out var ro) && ro.GetBoolean() || a.TryGetProperty("destructiveHint", out _))
                .Should().BeTrue($"{name} says it reads, or whether it destroys");
        }
        tools.Select(t => t.GetProperty("title").GetString()).Should().OnlyHaveUniqueItems();
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

    // The connector test (17 Nov 2026): reading a whole paper took one get_block per block.
    [Fact]
    public async Task Get_document_gives_every_block_and_the_body_as_LML_in_pages()
    {
        await SeedUserAsync("mcp_getdoc");
        var doc = await SeedDocumentAsync("mcp_getdoc", "Tides");
        await SeedBlockAsync(doc.Id, "heading", """{"text":"Introduction","level":1}""", 0);
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The moon pulls the sea."}""", 1);
        await SeedBlockAsync(doc.Id, "bibliography", """{"text":"@book{darwin1898, author={G. Darwin}, title={The Tides}, year={1898}}"}""", 2);
        var client = CreateClientAs("mcp_getdoc");

        var all = await CallAsync(client, "get_document", new { documentId = doc.Id.ToString() });
        all.GetProperty("blockCount").GetInt32().Should().Be(3);
        all.GetProperty("blocks").EnumerateArray().Select(b => b.GetProperty("type").GetString()).Should().Equal("heading", "paragraph", "bibliography");
        all.GetProperty("blocks")[1].GetProperty("content").GetProperty("text").GetString().Should().Be("The moon pulls the sea.");
        var lml = all.GetProperty("lml").GetString()!;
        lml.Should().Contain("Introduction").And.Contain("The moon pulls the sea.").And.Contain("@bibliography").And.Contain("darwin1898");
        // The last page: no nextOffset (the connector leaves nulls out).
        (!all.TryGetProperty("nextOffset", out var none) || none.ValueKind == JsonValueKind.Null).Should().BeTrue();

        var first = await CallAsync(client, "get_document", new { documentId = doc.Id.ToString(), offset = 0, limit = 2 });
        first.GetProperty("blocks").GetArrayLength().Should().Be(2);
        first.GetProperty("nextOffset").GetInt32().Should().Be(2);
        var rest = await CallAsync(client, "get_document", new { documentId = doc.Id.ToString(), offset = 2, limit = 2 });
        rest.GetProperty("blocks").GetArrayLength().Should().Be(1);
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

        foreach (var tool in new[] { "get_outline", "get_document_settings", "get_latex", "check_document" })
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

    /// <summary>Documents for one owner in one go, each with its own last change and validation.</summary>
    private async Task<List<Document>> SeedDocumentsAsync(string ownerId, int count, Func<int, (DateTime Updated, int? Warnings, int Errors)> shape, string title = "Doc")
    {
        await using var db = CreateDbContext();
        var docs = Enumerable.Range(0, count).Select(i =>
        {
            var (updated, warnings, errors) = shape(i);
            return new Document
            {
                Id = Guid.NewGuid(), OwnerId = ownerId, Title = $"{title} {i:D3}", Language = "en", PaperSize = "a4",
                FontFamily = "serif", FontSize = 12, CreatedAt = updated, UpdatedAt = updated,
                ValidationWarningCount = warnings ?? 0, ValidationErrorCount = errors,
                ValidationCheckedAt = warnings is null ? null : updated,
            };
        }).ToList();
        db.Documents.AddRange(docs);
        await db.SaveChangesAsync();
        return docs;
    }

    private static List<Guid> Ids(JsonElement list) =>
        list.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task An_author_with_more_than_50_documents_pages_through_all_of_them()
    {
        // 16 Nov 2026: the owner's account returned exactly 50 and the real count was unknown.
        await SeedUserAsync("mcp_many");
        var start = DateTime.UtcNow.AddDays(-1);
        // Opened in the reverse order of their change, so "last opened" can't pass for "last changed".
        var docs = await SeedDocumentsAsync("mcp_many", 57, i => (start.AddMinutes(-i), null, 0));
        await using (var db = CreateDbContext())
        {
            foreach (var d in db.Documents.Where(d => d.OwnerId == "mcp_many"))
                d.LastOpenedAt = DateTime.UtcNow.AddMinutes(-(57 - int.Parse(d.Title[^3..])));
            await db.SaveChangesAsync();
        }
        var client = CreateClientAs("mcp_many");

        var first = await CallAsync(client, "list_documents", new { limit = 50 });
        first.GetProperty("total").GetInt32().Should().Be(57);
        first.GetProperty("offset").GetInt32().Should().Be(0);
        first.GetProperty("sort").GetString().Should().Be("changed");
        first.GetProperty("nextOffset").GetInt32().Should().Be(50);
        Ids(first).Should().Equal(docs.Take(50).Select(d => d.Id), "most recently changed first");

        var second = await CallAsync(client, "list_documents", new { limit = 50, offset = 50 });
        second.GetProperty("total").GetInt32().Should().Be(57);
        (second.TryGetProperty("nextOffset", out var n) && n.ValueKind != JsonValueKind.Null).Should().BeFalse("it is the last page");
        Ids(second).Should().Equal(docs.Skip(50).Select(d => d.Id));

        // A page in the middle, and past the end.
        Ids(await CallAsync(client, "list_documents", new { limit = 5, offset = 20 })).Should().Equal(docs.Skip(20).Take(5).Select(d => d.Id));
        var beyond = await CallAsync(client, "list_documents", new { offset = 500 });
        beyond.GetProperty("documents").GetArrayLength().Should().Be(0);
        beyond.GetProperty("total").GetInt32().Should().Be(57);
    }

    [Fact]
    public async Task The_total_counts_only_what_the_search_finds()
    {
        await SeedUserAsync("mcp_total");
        var start = DateTime.UtcNow.AddDays(-1);
        await SeedDocumentsAsync("mcp_total", 3, i => (start.AddMinutes(-i), null, 0), "Tides");
        await SeedDocumentsAsync("mcp_total", 4, i => (start.AddMinutes(-i), null, 0), "Orbits");
        var client = CreateClientAs("mcp_total");

        var tides = await CallAsync(client, "list_documents", new { search = "tides", limit = 2 });
        tides.GetProperty("total").GetInt32().Should().Be(3);
        tides.GetProperty("nextOffset").GetInt32().Should().Be(2);
        tides.GetProperty("documents").EnumerateArray().Should().OnlyContain(d => d.GetProperty("title").GetString()!.StartsWith("Tides"));
    }

    [Fact]
    public async Task Documents_sort_by_their_last_validations_warnings()
    {
        // The owner, 16 Nov 2026: "documents ordered by max warnings".
        await SeedUserAsync("mcp_warn");
        var start = DateTime.UtcNow.AddDays(-1);
        int?[] warnings = [1, 7, null, 3, 7];
        int[] errors = [0, 0, 0, 2, 1];
        var docs = await SeedDocumentsAsync("mcp_warn", 5, i => (start.AddMinutes(-i), warnings[i], errors[i]));
        var client = CreateClientAs("mcp_warn");

        var byWarnings = await CallAsync(client, "list_documents", new { sort = "warnings" });
        byWarnings.GetProperty("sort").GetString().Should().Be("warnings");
        // 7 (1 error) before 7 (no error), then 3, 1, and the never-validated one last.
        Ids(byWarnings).Should().Equal(docs[4].Id, docs[1].Id, docs[3].Id, docs[0].Id, docs[2].Id);
        var top = byWarnings.GetProperty("documents")[0];
        top.GetProperty("validation").GetProperty("warnings").GetInt32().Should().Be(7);
        top.GetProperty("validation").GetProperty("errors").GetInt32().Should().Be(1);
        // Never validated: no validation (the serializer leaves nulls out).
        (byWarnings.GetProperty("documents")[4].TryGetProperty("validation", out var v) && v.ValueKind != JsonValueKind.Null)
            .Should().BeFalse();

        Ids(await CallAsync(client, "list_documents", new { sort = "errors", limit = 2 })).Should().Equal(docs[3].Id, docs[4].Id);

        var paged = await CallAsync(client, "list_documents", new { sort = "warnings", limit = 2, offset = 2 });
        Ids(paged).Should().Equal(docs[3].Id, docs[0].Id);

        var wrong = await CallAsync(client, "list_documents", new { sort = "size" });
        wrong.GetProperty("error").GetString().Should().Contain("Unknown sort");
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
