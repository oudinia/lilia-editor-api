using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// MCP hardening T3 (cloud session's handoff, 12 Oct): what agents are told and what they can write.
/// </summary>
[Collection("Integration")]
public class McpAgentFacingTests : IntegrationTestBase
{
    public McpAgentFacingTests(TestDatabaseFixture fixture) : base(fixture) { }

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

    [Theory]
    [InlineData("""{"text":"E = mc^2"}""", "equation", "latex", "E = mc^2")]
    [InlineData("""{"text":"$x+1$"}""", "equation", "latex", "x+1")]
    [InlineData("""{"latex":"a^2"}""", "paragraph", "text", "$a^2$")]
    public void A_type_change_carries_the_field_across(string content, string type, string field, string expected)
    {
        var mapped = JsonDocument.Parse(LiliaMcpWriteTools.MapForType(content, type)).RootElement;
        mapped.GetProperty(field).GetString().Should().Be(expected);
    }

    [Fact]
    public void Content_already_in_the_new_shape_is_left_alone() =>
        LiliaMcpWriteTools.MapForType("""{"latex":"x","label":"eq:x"}""", "equation").Should().Be("""{"latex":"x","label":"eq:x"}""");

    [Fact]
    public async Task An_unknown_block_type_is_refused_and_nothing_is_stored()
    {
        await SeedUserAsync("mcpt3_types");
        var doc = await SeedDocumentAsync("mcpt3_types");
        var r = await CallAsync(CreateClientAs("mcpt3_types"), "add_block",
            new { documentId = doc.Id.ToString(), type = "bogus", content = """{"text":"x"}""" });
        r.GetProperty("category").GetString().Should().Be("invalid");
        r.GetProperty("error").GetString().Should().Contain("no block type 'bogus'");
        await using var db = CreateDbContext();
        (await db.Blocks.CountAsync(b => b.DocumentId == doc.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Create_document_keeps_its_title_over_the_first_heading()
    {
        await SeedUserAsync("mcpt3_title");
        var r = await CallAsync(CreateClientAs("mcpt3_title"), "create_document",
            new { title = "My Survey", lml = "@heading[level=1]\n  Introduction\n@paragraph\n  Text." });
        var id = r.GetProperty("id").GetGuid();
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id)).Title.Should().Be("My Survey");
    }

    [Fact]
    public async Task A_block_alone_renders_under_its_documents_class()
    {
        await SeedUserAsync("mcpt3_class");
        var doc = await SeedDocumentAsync("mcpt3_class", "Book");
        await using (var db = CreateDbContext())
        {
            var d = await db.Documents.FirstAsync(x => x.Id == doc.Id);
            d.LatexDocumentClass = "book";
            d.DocumentCategory = "book";
            await db.SaveChangesAsync();
        }
        var heading = await SeedBlockAsync(doc.Id, "heading", """{"text":"Start","level":1}""", 0);
        var r = await CallAsync(CreateClientAs("mcpt3_class"), "get_lilia_latex",
            new { documentId = doc.Id.ToString(), blockId = heading.Id.ToString() });
        r.GetProperty("latex").GetString().Should().Contain(@"\chapter");
    }

    [Fact]
    public void Lml_takes_an_unnumbered_heading_and_the_four_missing_types()
    {
        var r = new LmlTextParser().Parse(string.Join("\n",
            "@heading[level=1, numbered=false]",
            "  Preface",
            "@algorithm[title=\"Search\", label=alg:s, lineNumbers=true]",
            "  \\STATE x \\gets 0",
            "  \\RETURN x",
            "@footnote",
            "  A note.",
            "@callout[title=\"Careful\", variant=warning]",
            "  Mind the gap.",
            "@embed",
            "  \\vspace{1em}"));
        r.Blocks.Select(b => b.Type).Should().Equal("heading", "algorithm", "footnote", "callout", "embed");
        string J(int i) => JsonSerializer.Serialize(r.Blocks[i].Content, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        J(0).Should().Contain("\"numbered\":false");
        J(1).Should().Contain("alg:s").And.Contain(@"\\STATE x \\gets 0\n\\RETURN x").And.Contain("\"lineNumbers\":true");
        J(3).Should().Contain("warning").And.Contain("Mind the gap.");
        J(4).Should().Contain(@"\\vspace{1em}");
        r.Warnings.Should().NotContain(w => w.Contains("Unknown block type"));
    }
}
