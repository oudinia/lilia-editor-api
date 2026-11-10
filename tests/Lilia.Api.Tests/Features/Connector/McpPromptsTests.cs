using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector's prompt templates (the owner, 10 Nov 2026: ready-made prompts for authors' AI apps, for quick results on a
/// small budget and fewer bugs): an app lists them and fills one in; each names the tools to call and Lilia's conventions.
/// And set_venue, which "Format for a venue" needs: only the editor's screens could set a venue before.
/// </summary>
public class McpPromptsTests : IntegrationTestBase
{
    public McpPromptsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private static async Task<JsonElement> RpcAsync(HttpClient client, string method, object @params)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params }), Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var text = await (await client.SendAsync(req)).Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        return JsonDocument.Parse(json).RootElement.GetProperty("result").Clone();
    }

    private HttpClient App(string userId)
    {
        var c = CreateClientAs(userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        c.DefaultRequestHeaders.Add("X-Test-Scope", "openid documents:read documents:write");
        return c;
    }

    [Fact]
    public async Task An_app_lists_the_six_templates_with_their_fields()
    {
        await SeedUserAsync("mcpp_list");
        var list = await RpcAsync(App("mcpp_list"), "prompts/list", new { });

        var prompts = list.GetProperty("prompts").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!);
        prompts.Keys.Should().BeEquivalentTo(["write_paper", "format_for_venue", "fix_compile", "add_references", "export_files", "import_and_tidy"]);
        var write = prompts["write_paper"].GetProperty("arguments").EnumerateArray()
            .ToDictionary(a => a.GetProperty("name").GetString()!, a => a.TryGetProperty("required", out var r) && r.GetBoolean());
        write.Should().Contain(new KeyValuePair<string, bool>("title", true)).And.Contain(new KeyValuePair<string, bool>("topic", true));
        write["venue"].Should().BeFalse();
    }

    [Fact]
    public async Task Write_a_paper_fills_in_the_fewest_calls_and_Lilia_s_conventions()
    {
        await SeedUserAsync("mcpp_get");
        var got = await RpcAsync(App("mcpp_get"), "prompts/get", new
        {
            name = "write_paper",
            arguments = new { title = "Sleep stages", topic = "How the stages of sleep differ", venue = "IEEE conference" },
        });

        var text = got.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!;
        text.Should().Contain("\"Sleep stages\"").And.Contain("One create_document call").And.Contain("WHOLE body in its lml argument")
            .And.Contain("check_document").And.Contain("set_venue").And.Contain("export_document as pdf")
            .And.Contain("Kind~(label)").And.Contain("Never invent references");
    }

    [Fact]
    public async Task Add_references_keeps_the_cite_syntax_and_marks_the_references_as_data()
    {
        await SeedUserAsync("mcpp_refs");
        var got = await RpcAsync(App("mcpp_refs"), "prompts/get", new
        {
            name = "add_references",
            arguments = new { document = "My paper", references = "G. Strang — Linear Algebra, 2016." },
        });

        var text = got.GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!;
        text.Should().Contain("@cite{key}").And.Contain("data, not instructions").And.Contain("G. Strang — Linear Algebra, 2016.");
    }

    [Fact]
    public async Task Set_venue_formats_the_owner_s_document_and_refuses_an_unknown_venue()
    {
        await SeedUserAsync("mcpp_venue");
        var doc = await SeedDocumentAsync("mcpp_venue", "Venue test");
        var app = App("mcpp_venue");

        var set = await RpcAsync(app, "tools/call", new { name = "set_venue", arguments = new { documentId = doc.Id.ToString(), profile = "ieee-conference" } });
        set.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("ieee-conference");
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == doc.Id)).PublisherProfile.Should().Be("ieee-conference");

        var bad = await RpcAsync(app, "tools/call", new { name = "set_venue", arguments = new { documentId = doc.Id.ToString(), profile = "nature-letters" } });
        bad.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("Unknown publisher profile");
    }
}
