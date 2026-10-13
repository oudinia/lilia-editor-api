using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Integration.Mcp;

/// <summary>
/// MCP step 3 (the user, 12 Oct: "go ahead with step 3"): tools that change a document. Ask Lilia's
/// own writes, for an owner or editor only; an app needs the documents:write scope (edits allowed
/// on the consent page); a version is saved before an app's first change.
/// </summary>
[Collection("Integration")]
public class McpWriteToolsTests : IntegrationTestBase
{
    public McpWriteToolsTests(TestDatabaseFixture fixture) : base(fixture) { }

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
        var res = await client.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var json = text.TrimStart().StartsWith('{') ? text : text.Split('\n').Last(l => l.StartsWith("data:"))["data:".Length..];
        var body = JsonDocument.Parse(json).RootElement;
        return JsonDocument.Parse(body.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!).RootElement.Clone();
    }

    private HttpClient App(string userId, string? scope)
    {
        var c = CreateClientAs(userId);
        c.DefaultRequestHeaders.Add("X-Test-ClientId", "connected-app-test-claude");
        if (scope is not null) c.DefaultRequestHeaders.Add("X-Test-Scope", scope);
        return c;
    }

    private async Task<int> BlockCountAsync(Guid docId)
    {
        await using var db = CreateDbContext();
        return await db.Blocks.CountAsync(b => b.DocumentId == docId);
    }

    [Fact]
    public async Task An_app_allowed_to_edit_adds_a_block_and_an_undo_point_is_saved_first()
    {
        await SeedUserAsync("mcpw_owner");
        var doc = await SeedDocumentAsync("mcpw_owner", "Edited by Claude");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First."}""", 0);
        var app = App("mcpw_owner", "openid documents:read documents:write");

        var r = await CallAsync(app, "add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"Added by an app."}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r.GetProperty("changedBlocks").GetArrayLength().Should().Be(1);
        var undo = r.GetProperty("undo").GetProperty("versionId").GetGuid();
        (await BlockCountAsync(doc.Id)).Should().Be(2);

        // A second change in the same burst reuses the same undo point.
        var r2 = await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "Retitled" });
        r2.GetProperty("undo").GetProperty("versionId").GetGuid().Should().Be(undo);

        await using var db = CreateDbContext();
        (await db.DocumentVersions.CountAsync(v => v.DocumentId == doc.Id)).Should().Be(1);
    }

    [Fact]
    public async Task For_now_an_app_edits_without_the_write_scope()
    {
        // The user, 12 Oct: "for now, let's give the mcp write capability also".
        await SeedUserAsync("mcpw_no_scope");
        var doc = await SeedDocumentAsync("mcpw_no_scope");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var r = await CallAsync(App("mcpw_no_scope", "openid email profile"), "add_block",
            new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"From an app."}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        (await BlockCountAsync(doc.Id)).Should().Be(2);
    }

    [Fact]
    public void With_the_scope_required_an_app_connected_for_reading_only_may_not_write()
    {
        System.Security.Claims.ClaimsPrincipal App(string scope) => new(new System.Security.Claims.ClaimsIdentity(new[]
        {
            new System.Security.Claims.Claim("sub", "u"),
            new System.Security.Claims.Claim("client_id", "connected-app-test-claude"),
            new System.Security.Claims.Claim("scope", scope),
        }, "Test"));
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid email"), requireScope: true).Should().BeFalse();
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid documents:write"), requireScope: true).Should().BeTrue();
        Lilia.Api.Mcp.LiliaMcpWriteTools.AppMayWrite(App("openid email"), requireScope: false).Should().BeTrue();
    }

    [Fact]
    public async Task A_viewer_cannot_edit_even_with_the_scope()
    {
        await SeedUserAsync("mcpw_doc_owner");
        await SeedUserAsync("mcpw_viewer");
        var doc = await SeedDocumentAsync("mcpw_doc_owner");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Owner's text."}""", 0);
        await using (var db = CreateDbContext())
        {
            var viewer = await db.Roles.FirstOrDefaultAsync(r => r.Name == "viewer")
                ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = "viewer" }).Entity;
            db.DocumentCollaborators.Add(new DocumentCollaborator
            {
                Id = Guid.NewGuid(), DocumentId = doc.Id, UserId = "mcpw_viewer", RoleId = viewer.Id,
                InvitedBy = "mcpw_doc_owner", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var r = await CallAsync(App("mcpw_viewer", "documents:write"), "edit_block",
            new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), content = """{"text":"Changed."}""" });
        r.GetProperty("error").GetString().Should().Contain("view-only");
    }

    [Fact]
    public async Task Settings_change_through_one_json_object()
    {
        await SeedUserAsync("mcpw_settings");
        var doc = await SeedDocumentAsync("mcpw_settings");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var r = await CallAsync(CreateClientAs("mcpw_settings"), "set_document_settings",
            new { documentId = doc.Id.ToString(), settings = new { fontSize = 12, marginLeft = "2cm" } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using var db = CreateDbContext();
        var saved = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
        saved.FontSize.Should().Be(12);
        saved.MarginLeft.Should().Be("2cm");
    }

    // MCP-31: heading numbering is a document setting the author (or the app) chooses.
    [Fact]
    public async Task Heading_numbering_is_a_setting()
    {
        await SeedUserAsync("mcpw_numbering");
        var doc = await SeedDocumentAsync("mcpw_numbering");
        var client = CreateClientAs("mcpw_numbering");
        var r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = "2" } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        (await CallAsync(client, "get_document_settings", new { documentId = doc.Id.ToString() })).GetProperty("headingNumbering").GetString().Should().Be("2");
        await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = "default" } });
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).HeadingNumbering.Should().BeNull();
    }

    // From the MCP re-test (13 Oct): {"headingNumbering": 1} failed ("could not be converted to
    // System.String"), the object sent as a JSON string changed nothing, and a misspelt field was ignored.
    [Fact]
    public async Task Settings_take_numbers_for_choices_a_json_string_and_name_unknown_fields()
    {
        await SeedUserAsync("mcpw_shapes");
        var doc = await SeedDocumentAsync("mcpw_shapes");
        var client = CreateClientAs("mcpw_shapes");
        var r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { headingNumbering = 1 } });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = """{"pageNumbering":"roman","FontSize":12}""" });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using (var db = CreateDbContext())
        {
            var saved = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id);
            saved.HeadingNumbering.Should().Be(1);
            saved.PageNumbering.Should().Be("roman");
            saved.FontSize.Should().Be(12);
        }
        r = await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { fontSize = 11, pageNumber = "none" } });
        r.GetProperty("error").GetString().Should().Contain("'pageNumber'").And.Contain("pageNumbering");
        await using (var db = CreateDbContext())
            (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).FontSize.Should().Be(12, "nothing changes when a field is unknown");
    }

    // MCP-86: null on a text setting clears it, as get_document_settings reports unset.
    [Fact]
    public async Task Null_clears_a_text_setting()
    {
        await SeedUserAsync("mcpw_null");
        var doc = await SeedDocumentAsync("mcpw_null");
        var client = CreateClientAs("mcpw_null");
        await CallAsync(client, "set_document_settings", new { documentId = doc.Id.ToString(), settings = new { marginLeft = "2cm" } });
        var r = await CallAsync(client, "set_document_settings",
            new { documentId = doc.Id.ToString(), settings = JsonDocument.Parse("""{"marginLeft":null}""").RootElement });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        await using var db = CreateDbContext();
        (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == doc.Id)).MarginLeft.Should().BeNull();
    }

    // MCP-82 / MCP-83: a block that prints nothing makes printsCorrectly false, and export_document
    // reports the check that was just run instead of "never checked".
    [Fact]
    public async Task The_check_counts_unprinted_blocks_and_export_remembers_it()
    {
        await SeedUserAsync("mcpw_check");
        var doc = await SeedDocumentAsync("mcpw_check");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        await SeedBlockAsync(doc.Id, "cover", """{"text":"A cover."}""", 1);
        var client = CreateClientAs("mcpw_check");
        var check = await CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeTrue(check.ToString());
        check.GetProperty("printsCorrectly").GetBoolean().Should().BeFalse();
        check.GetProperty("unprinted").ToString().Should().Contain("cover");
        var export = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        export.GetProperty("lastCheck").GetProperty("source").GetString().Should().Be("check_document");
        export.GetProperty("lastCheck").GetProperty("printsCorrectly").GetBoolean().Should().BeFalse();
    }

    // MCP-88 (run 3): an app calling check_document and export_document together got "Never
    // checked" from the export; it now waits for the check that is still compiling.
    [Fact]
    public async Task An_export_during_a_check_reports_that_check()
    {
        await SeedUserAsync("mcpw_together");
        var doc = await SeedDocumentAsync("mcpw_together");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        var client = CreateClientAs("mcpw_together");
        var check = CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        await Task.Delay(400);
        var export = await CallAsync(CreateClientAs("mcpw_together"), "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        (await check).GetProperty("compiles").GetBoolean().Should().BeTrue();
        export.GetProperty("lastCheck").GetProperty("source").GetString().Should().Be("check_document", export.ToString());
    }

    // MCP-72: no pdf link for a version the check just found broken; the LaTeX still exports.
    [Fact]
    public async Task No_pdf_link_for_a_document_that_does_not_compile()
    {
        await SeedUserAsync("mcpw_broken");
        var doc = await SeedDocumentAsync("mcpw_broken");
        await SeedBlockAsync(doc.Id, "embed", """{"latex":"\\notACommandAnywhere"}""", 0);
        var client = CreateClientAs("mcpw_broken");
        var check = await CallAsync(client, "check_document", new { documentId = doc.Id.ToString() });
        check.GetProperty("compiles").GetBoolean().Should().BeFalse(check.ToString());
        var pdf = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "pdf" });
        pdf.GetProperty("category").GetString().Should().Be("does-not-compile", pdf.ToString());
        pdf.TryGetProperty("url", out var u).Should().BeTrue();
        u.GetString().Should().NotContain("/connector/files/");
        var tex = await CallAsync(client, "export_document", new { documentId = doc.Id.ToString(), format = "tex" });
        tex.GetProperty("url").GetString().Should().Contain("/connector/files/");
    }

    // MCP-85 / MCP-87: get_lilia_latex says the engine the check uses; a list's outline is its text.
    [Fact]
    public async Task The_engine_and_the_outline_are_told_plainly()
    {
        await SeedUserAsync("mcpw_engine");
        var doc = await SeedDocumentAsync("mcpw_engine");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"数据 means data."}""", 0);
        await SeedBlockAsync(doc.Id, "list", """{"items":["first item","second item"],"ordered":false}""", 1);
        var client = CreateClientAs("mcpw_engine");
        (await CallAsync(client, "get_lilia_latex", new { documentId = doc.Id.ToString() }))
            .GetProperty("engine").GetString().Should().Be("xelatex");
        var outline = await CallAsync(client, "get_outline", new { documentId = doc.Id.ToString() });
        var snippet = outline.GetProperty("blocks").EnumerateArray().First(b => b.GetProperty("type").GetString() == "list").GetProperty("snippet").GetString();
        snippet.Should().Contain("first item").And.NotStartWith("{");
    }

    [Fact]
    public async Task An_app_creates_a_document_with_its_body()
    {
        await SeedUserAsync("mcpw_creator");
        var r = await CallAsync(CreateClientAs("mcpw_creator"), "create_document",
            new { title = "Written by an app", lml = "@heading[level=1]\n  Introduction\n@paragraph\n  A first paragraph." });
        r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());
        var id = r.GetProperty("id").GetGuid();
        r.GetProperty("url").GetString().Should().EndWith($"/d/{id}");
        await using var db = CreateDbContext();
        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == id);
        doc.OwnerId.Should().Be("mcpw_creator");
        (await db.Blocks.CountAsync(b => b.DocumentId == id && b.Type == "paragraph")).Should().Be(1);
    }
}
