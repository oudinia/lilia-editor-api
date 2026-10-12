using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// Import and export over the connector without the file in the conversation (the user, 12 Oct:
/// "import and export must not consume too many tokens"): the app gets links; the file goes
/// between the author's browser and Lilia.
/// </summary>
[Collection("Integration")]
public class McpTransfersTests : IntegrationTestBase
{
    public McpTransfersTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> CallAsync(HttpClient client, string tool, object args)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
                Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private static string PathOf(string url) => new Uri(url).PathAndQuery;

    [Fact]
    public async Task Export_gives_a_link_and_the_link_gives_the_file_without_signing_in()
    {
        await SeedUserAsync("mcpx_owner");
        var doc = await SeedDocumentAsync("mcpx_owner", "Exported paper");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Exported through a link."}""", 0);

        var r = await CallAsync(CreateClientAs("mcpx_owner"), "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        var url = r.GetProperty("url").GetString()!;
        url.Should().StartWith("https://editor.liliaeditor.com/api/connector/files/");
        r.GetProperty("lastCheck").GetProperty("note").GetString().Should().Contain("check_document");

        var res = await CreateAnonymousClient().GetAsync(PathOf(url));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentDisposition!.FileNameStar.Should().EndWith(".tex");
        (await res.Content.ReadAsStringAsync()).Should().Contain("Exported through a link.").And.Contain("\\documentclass");
    }

    [Fact]
    public async Task A_made_up_link_and_someone_elses_document_get_nothing()
    {
        var made = await CreateAnonymousClient().GetAsync($"/api/connector/files/{new string('a', 48)}");
        made.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Opened in a browser, often a phone: a sentence, not JSON.
        made.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        await SeedUserAsync("mcpx_alice");
        await SeedUserAsync("mcpx_mallory");
        var doc = await SeedDocumentAsync("mcpx_alice");
        var r = await CallAsync(CreateClientAs("mcpx_mallory"), "export_document", new { documentId = doc.Id.ToString() });
        r.GetProperty("error").GetString().Should().Contain("No document");
    }

    [Fact]
    public async Task Import_through_the_drop_page_creates_the_authors_document_and_the_app_reads_its_id()
    {
        await SeedUserAsync("mcpx_importer");
        await SeedUserAsync("mcpx_other");
        var author = CreateClientAs("mcpx_importer");

        var start = await CallAsync(author, "start_import", new { });
        var importId = start.GetProperty("importId").GetString()!;
        start.GetProperty("url").GetString().Should().Be($"https://editor.liliaeditor.com/connect/import/{importId}");
        (await CallAsync(author, "import_status", new { importId })).GetProperty("status").GetString().Should().Be("waiting");

        // The drop page: only the author it was issued to.
        (await CreateClientAs("mcpx_other").GetAsync($"/api/connector/imports/{importId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await author.GetAsync($"/api/connector/imports/{importId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var tex = "\\documentclass{article}\\title{Dropped paper}\\begin{document}\\maketitle\\section{Intro}Hello from a dropped file.\\end{document}";
        var post = await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = tex, format = "latex", filename = "dropped.tex" });
        post.StatusCode.Should().Be(HttpStatusCode.OK, await post.Content.ReadAsStringAsync());

        var status = await CallAsync(author, "import_status", new { importId });
        status.GetProperty("status").GetString().Should().Be("done");
        var id = status.GetProperty("documentId").GetGuid();
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id)).OwnerId.Should().Be("mcpx_importer");

        // A link is used once.
        (await author.PostAsJsonAsync($"/api/connector/imports/{importId}", new { content = tex, format = "latex", filename = "again.tex" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_apps_token_cannot_upload_on_the_drop_page()
    {
        await SeedUserAsync("mcpx_app");
        var start = await CallAsync(CreateClientAs("mcpx_app"), "start_import", new { });
        var app = CreateClientAs("mcpx_app");
        app.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        var res = await app.PostAsJsonAsync($"/api/connector/imports/{start.GetProperty("importId").GetString()}",
            new { content = "\\documentclass{article}\\begin{document}x\\end{document}", format = "latex", filename = "x.tex" });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the file comes from the author's browser, not the app");
    }
}
