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
    public async Task An_app_connected_for_reading_only_is_told_to_reconnect()
    {
        await SeedUserAsync("mcpw_reader_app");
        var doc = await SeedDocumentAsync("mcpw_reader_app");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Keep me."}""", 0);
        var r = await CallAsync(App("mcpw_reader_app", "openid email profile"), "remove_block",
            new { documentId = doc.Id.ToString(), blockId = Guid.NewGuid().ToString() });
        r.GetProperty("error").GetString().Should().Contain("connect again");
        (await BlockCountAsync(doc.Id)).Should().Be(1);
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
}
