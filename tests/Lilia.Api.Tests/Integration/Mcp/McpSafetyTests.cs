using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// MCP hardening T0 (cloud session's handoff, 12 Oct): unsafe LaTeX refused when it is written,
/// a per-user rate limit on the connector, and a check that says what happened.
/// </summary>
[Collection("Integration")]
public class McpSafetyTests : IntegrationTestBase
{
    public McpSafetyTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static HttpRequestMessage Call(string tool, object args) => new(HttpMethod.Post, "/api/mcp")
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = tool, arguments = args } }),
            Encoding.UTF8, "application/json"),
        Headers = { Accept = { new MediaTypeWithQualityHeaderValue("application/json"), new MediaTypeWithQualityHeaderValue("text/event-stream") } },
    };

    private static async Task<JsonElement> ResultAsync(HttpResponseMessage res)
    {
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    [Theory]
    [InlineData("""{"text":"x \\immediate\\write18{rm -rf /}"}""")]
    [InlineData("""{"latex":"\\directlua{os.execute('id')}"}""")]
    [InlineData("""{"text":"\\input{/etc/passwd}"}""")]
    public async Task Unsafe_latex_is_refused_when_written_and_nothing_is_stored(string content)
    {
        await SeedUserAsync("mcps_writer");
        var doc = await SeedDocumentAsync("mcps_writer");
        var r = await ResultAsync(await CreateClientAs("mcps_writer").SendAsync(
            Call("add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content })));
        r.GetProperty("category").GetString().Should().Be("refused");
        r.GetProperty("error").GetString().Should().StartWith("Lilia refuses this LaTeX");
        await using var db = CreateDbContext();
        (await db.Blocks.CountAsync(b => b.DocumentId == doc.Id)).Should().Be(0);
    }

    [Fact]
    public async Task A_new_document_with_unsafe_lml_is_refused()
    {
        await SeedUserAsync("mcps_creator");
        var r = await ResultAsync(await CreateClientAs("mcps_creator").SendAsync(
            Call("create_document", new { title = "x", lml = "@paragraph\n  \\write18{id}" })));
        r.GetProperty("category").GetString().Should().Be("refused");
        await using var db = CreateDbContext();
        (await db.Documents.CountAsync(d => d.OwnerId == "mcps_creator")).Should().Be(0);
    }

    [Fact]
    public async Task Check_document_says_refused_for_unsafe_latex_already_stored()
    {
        await SeedUserAsync("mcps_checker");
        var doc = await SeedDocumentAsync("mcps_checker");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"\\immediate\\write18{id}"}""", 0);
        var r = await ResultAsync(await CreateClientAs("mcps_checker").SendAsync(Call("check_document", new { documentId = doc.Id.ToString() })));
        r.GetProperty("category").GetString().Should().Be("refused");
        r.GetProperty("error").GetString().Should().NotContain("Try again");
    }

    [Fact]
    public async Task The_connector_is_rate_limited_per_user()
    {
        await SeedUserAsync("mcps_busy");
        await SeedUserAsync("mcps_calm");
        var busy = CreateClientAs("mcps_busy");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 61; i++)
            statuses.Add((await busy.SendAsync(Call("search_help", new { query = "share" }))).StatusCode);
        statuses.Take(60).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Last().Should().Be(HttpStatusCode.TooManyRequests);
        // Someone else is not affected.
        (await CreateClientAs("mcps_calm").SendAsync(Call("search_help", new { query = "share" }))).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
