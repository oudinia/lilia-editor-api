using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// The connector test (17 Nov 2026), items D and G. D: every edit's reply gave an undo point that only the author could
/// restore, in History; list_versions and restore_version let the assistant undo, and a restore is itself undoable.
/// G: collaboration happens in comments and the assistant couldn't see them; list_comments, add_comment,
/// reply_to_comment and resolve_comment follow the editor's comment rules (viewers comment too, resolving is for the
/// comment's author or the owner).
/// </summary>
public class McpVersionsAndCommentsTests : IntegrationTestBase
{
    public McpVersionsAndCommentsTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string EditScope = "openid documents:read documents:write";

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

    private HttpClient App(string userId, string? scope = EditScope)
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

    private async Task ShareAsync(Guid docId, string ownerId, string userId, string role)
    {
        await using var db = CreateDbContext();
        var r = await db.Roles.FirstOrDefaultAsync(x => x.Name == role)
            ?? db.Roles.Add(new Role { Id = Guid.NewGuid(), Name = role }).Entity;
        db.DocumentCollaborators.Add(new DocumentCollaborator
        {
            Id = Guid.NewGuid(), DocumentId = docId, UserId = userId, RoleId = r.Id, InvitedBy = ownerId, CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static void NoError(JsonElement r) => r.TryGetProperty("error", out _).Should().BeFalse(r.ToString());

    // ── D. versions ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_app_undoes_its_edit_with_restore_version_and_the_restore_can_be_undone_too()
    {
        await SeedUserAsync("mcpv_owner");
        var doc = await SeedDocumentAsync("mcpv_owner", "Undo me");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"First."}""", 0);
        var app = App("mcpv_owner");

        var edit = await CallAsync(app, "add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"Added."}""" });
        NoError(edit);
        var undo = edit.GetProperty("undo").GetProperty("versionId").GetGuid();
        edit.GetProperty("undo").GetProperty("note").GetString().Should().Contain("restore_version");
        (await BlockCountAsync(doc.Id)).Should().Be(2);

        var list = await CallAsync(app, "list_versions", new { documentId = doc.Id.ToString() });
        NoError(list);
        var saved = list.GetProperty("versions").EnumerateArray().Single(v => v.GetProperty("id").GetGuid() == undo);
        saved.GetProperty("name").GetString().Should().StartWith("Before ");
        saved.GetProperty("blockCount").GetInt32().Should().Be(1, "the undo point holds the document before the edit");
        saved.GetProperty("savedBy").GetString().Should().Be("mcpv_owner");

        var restored = await CallAsync(app, "restore_version", new { documentId = doc.Id.ToString(), versionId = undo.ToString() });
        NoError(restored);
        restored.GetProperty("restored").GetProperty("versionId").GetGuid().Should().Be(undo);
        restored.GetProperty("blockCount").GetInt32().Should().Be(1);
        (await BlockCountAsync(doc.Id)).Should().Be(1, "the added block is gone");
        var undoRestore = restored.GetProperty("undo").GetProperty("versionId").GetGuid();
        undoRestore.Should().NotBe(undo, "restoring the burst's own undo point must leave something to go back to");
        restored.GetProperty("undo").GetProperty("note").GetString().Should().Contain("restore_version");

        // The restore itself is undone the same way.
        var again = await CallAsync(app, "restore_version", new { documentId = doc.Id.ToString(), versionId = undoRestore.ToString() });
        NoError(again);
        (await BlockCountAsync(doc.Id)).Should().Be(2);
        await using (var db = CreateDbContext())
            (await db.Blocks.AsNoTracking().Where(b => b.DocumentId == doc.Id).ToListAsync())
                .Select(b => b.Content.RootElement.GetProperty("text").GetString()).Should().BeEquivalentTo("First.", "Added.");

        // After a restore, the next edit saves a new undo point: the restored text.
        var next = await CallAsync(app, "set_title", new { documentId = doc.Id.ToString(), title = "Retitled" });
        next.GetProperty("undo").GetProperty("versionId").GetGuid().Should().NotBe(undo).And.NotBe(undoRestore);
    }

    [Fact]
    public async Task List_versions_gives_the_newest_first_and_at_most_the_limit()
    {
        await SeedUserAsync("mcpv_list");
        var doc = await SeedDocumentAsync("mcpv_list", "Three versions");
        await using (var db = CreateDbContext())
        {
            for (var n = 1; n <= 3; n++)
            {
                var blocks = Enumerable.Range(0, n).Select(i => new { id = Guid.NewGuid(), type = "paragraph", content = new { text = $"p{i}" }, sortOrder = i });
                db.DocumentVersions.Add(new DocumentVersion
                {
                    Id = Guid.NewGuid(), DocumentId = doc.Id, VersionNumber = n, Name = $"Draft {n}", CreatedBy = "mcpv_list",
                    Snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new { blocks })), CreatedAt = DateTime.UtcNow.AddMinutes(n),
                });
            }
            await db.SaveChangesAsync();
        }
        var client = CreateClientAs("mcpv_list");

        var all = await CallAsync(client, "list_versions", new { documentId = doc.Id.ToString() });
        all.GetProperty("total").GetInt32().Should().Be(3);
        all.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("name").GetString()).Should().Equal("Draft 3", "Draft 2", "Draft 1");
        all.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("blockCount").GetInt32()).Should().Equal(3, 2, 1);

        var two = await CallAsync(client, "list_versions", new { documentId = doc.Id.ToString(), limit = 2 });
        two.GetProperty("total").GetInt32().Should().Be(3);
        two.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("number").GetInt32()).Should().Equal(3, 2);
    }

    [Fact]
    public async Task A_viewer_lists_the_versions_but_cannot_restore_one()
    {
        await SeedUserAsync("mcpv_doc_owner");
        await SeedUserAsync("mcpv_viewer");
        var doc = await SeedDocumentAsync("mcpv_doc_owner");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Owner's text."}""", 0);
        var edit = await CallAsync(App("mcpv_doc_owner"), "add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"More."}""" });
        var undo = edit.GetProperty("undo").GetProperty("versionId").GetGuid();
        await ShareAsync(doc.Id, "mcpv_doc_owner", "mcpv_viewer", "viewer");
        var viewer = App("mcpv_viewer");

        var list = await CallAsync(viewer, "list_versions", new { documentId = doc.Id.ToString() });
        list.GetProperty("versions").EnumerateArray().Select(v => v.GetProperty("id").GetGuid()).Should().Contain(undo);

        var r = await CallAsync(viewer, "restore_version", new { documentId = doc.Id.ToString(), versionId = undo.ToString() });
        r.GetProperty("error").GetString().Should().Contain("view-only");
        (await BlockCountAsync(doc.Id)).Should().Be(2, "nothing was restored");
    }

    [Fact]
    public async Task Restore_version_refuses_wrong_ids_a_stranger_and_an_app_that_may_only_read()
    {
        await SeedUserAsync("mcpv_ids");
        await SeedUserAsync("mcpv_stranger");
        var doc = await SeedDocumentAsync("mcpv_ids");
        var other = await SeedDocumentAsync("mcpv_ids", "Another");
        await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Body."}""", 0);
        await SeedBlockAsync(other.Id, "paragraph", """{"text":"Elsewhere."}""", 0);
        var app = App("mcpv_ids");
        var otherUndo = (await CallAsync(app, "add_block", new { documentId = other.Id.ToString(), type = "paragraph", content = """{"text":"x"}""" }))
            .GetProperty("undo").GetProperty("versionId").GetGuid();
        var undo = (await CallAsync(app, "add_block", new { documentId = doc.Id.ToString(), type = "paragraph", content = """{"text":"y"}""" }))
            .GetProperty("undo").GetProperty("versionId").GetGuid();

        foreach (var wrong in new[] { Guid.NewGuid().ToString(), "not-a-guid", otherUndo.ToString() })
            (await CallAsync(app, "restore_version", new { documentId = doc.Id.ToString(), versionId = wrong }))
                .GetProperty("error").GetString().Should().Contain("No version", wrong);

        var stranger = App("mcpv_stranger");
        (await CallAsync(stranger, "list_versions", new { documentId = doc.Id.ToString() })).GetProperty("error").GetString().Should().Contain("No document");
        (await CallAsync(stranger, "restore_version", new { documentId = doc.Id.ToString(), versionId = undo.ToString() }))
            .GetProperty("error").GetString().Should().Contain("No document");

        (await CallAsync(App("mcpv_ids", "openid documents:read"), "restore_version", new { documentId = doc.Id.ToString(), versionId = undo.ToString() }))
            .GetProperty("error").GetString().Should().Contain("connect again");
        (await BlockCountAsync(doc.Id)).Should().Be(2, "nothing was restored");
    }

    // ── G. comments ──────────────────────────────────────────────────────

    [Fact]
    public async Task An_author_comments_replies_and_resolves_and_reads_it_all_back()
    {
        await SeedUserAsync("mcpc_owner", name: "Ada Lovelace");
        var doc = await SeedDocumentAsync("mcpc_owner", "Commented");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"The engine weaves algebraic patterns."}""", 0);
        var app = App("mcpc_owner");

        var added = await CallAsync(app, "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "  Cite Menabrea here.  " });
        NoError(added);
        var commentId = added.GetProperty("id").GetGuid();
        added.GetProperty("text").GetString().Should().Be("Cite Menabrea here.");

        var reply = await CallAsync(app, "reply_to_comment", new { documentId = doc.Id.ToString(), commentId = commentId.ToString(), text = "Done in the next draft." });
        NoError(reply);

        var list = await CallAsync(app, "list_comments", new { documentId = doc.Id.ToString() });
        NoError(list);
        list.GetProperty("open").GetInt32().Should().Be(1);
        var c = list.GetProperty("comments").EnumerateArray().Single();
        c.GetProperty("id").GetGuid().Should().Be(commentId);
        c.GetProperty("block").GetProperty("id").GetGuid().Should().Be(block.Id);
        c.GetProperty("block").GetProperty("snippet").GetString().Should().Contain("algebraic patterns");
        c.GetProperty("author").GetString().Should().Be("Ada Lovelace");
        c.GetProperty("byYou").GetBoolean().Should().BeTrue();
        c.GetProperty("resolved").GetBoolean().Should().BeFalse();
        c.GetProperty("replies").EnumerateArray().Single().GetProperty("text").GetString().Should().Be("Done in the next draft.");

        var resolved = await CallAsync(app, "resolve_comment", new { documentId = doc.Id.ToString(), commentId = commentId.ToString() });
        NoError(resolved);
        resolved.GetProperty("resolved").GetBoolean().Should().BeTrue();
        (await CallAsync(app, "resolve_comment", new { documentId = doc.Id.ToString(), commentId = commentId.ToString() }))
            .GetProperty("note").GetString().Should().Contain("already");

        var open = await CallAsync(app, "list_comments", new { documentId = doc.Id.ToString() });
        open.GetProperty("comments").GetArrayLength().Should().Be(0, "resolved comments are left out by default");
        open.GetProperty("resolved").GetInt32().Should().Be(1);
        var withResolved = await CallAsync(app, "list_comments", new { documentId = doc.Id.ToString(), includeResolved = true });
        withResolved.GetProperty("comments").EnumerateArray().Single().GetProperty("resolved").GetBoolean().Should().BeTrue();

        // In the author's name, in the editor's own table.
        await using var db = CreateDbContext();
        var saved = await db.Comments.Include(x => x.Replies).SingleAsync(x => x.Id == commentId);
        saved.UserId.Should().Be("mcpc_owner");
        saved.BlockId.Should().Be(block.Id);
        saved.Replies.Single().UserId.Should().Be("mcpc_owner");
    }

    [Fact]
    public async Task A_viewer_comments_and_replies_but_resolves_only_their_own()
    {
        await SeedUserAsync("mcpc_doc_owner", name: "Owner");
        await SeedUserAsync("mcpc_viewer", name: "Reviewer");
        var doc = await SeedDocumentAsync("mcpc_doc_owner");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"A claim."}""", 0);
        await ShareAsync(doc.Id, "mcpc_doc_owner", "mcpc_viewer", "viewer");
        var owner = App("mcpc_doc_owner");
        var viewer = App("mcpc_viewer");

        var ownersComment = (await CallAsync(owner, "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "Is this right?" }))
            .GetProperty("id").GetGuid();
        var viewersComment = await CallAsync(viewer, "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "Needs a source." });
        NoError(viewersComment);
        NoError(await CallAsync(viewer, "reply_to_comment", new { documentId = doc.Id.ToString(), commentId = ownersComment.ToString(), text = "I think so." }));

        var refused = await CallAsync(viewer, "resolve_comment", new { documentId = doc.Id.ToString(), commentId = ownersComment.ToString() });
        refused.GetProperty("error").GetString().Should().Contain("author or the document's owner");
        NoError(await CallAsync(viewer, "resolve_comment", new { documentId = doc.Id.ToString(), commentId = viewersComment.GetProperty("id").GetString() }));

        // The owner sees the reviewer's words, not as theirs.
        var list = await CallAsync(owner, "list_comments", new { documentId = doc.Id.ToString(), includeResolved = true });
        var mine = list.GetProperty("comments").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == ownersComment);
        mine.GetProperty("resolved").GetBoolean().Should().BeFalse();
        var answer = mine.GetProperty("replies").EnumerateArray().Single();
        answer.GetProperty("author").GetString().Should().Be("Reviewer");
        answer.GetProperty("byYou").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Comment_tools_refuse_wrong_ids_a_stranger_empty_text_and_an_app_that_may_only_read()
    {
        await SeedUserAsync("mcpc_ids");
        await SeedUserAsync("mcpc_stranger");
        var doc = await SeedDocumentAsync("mcpc_ids");
        var other = await SeedDocumentAsync("mcpc_ids", "Another");
        var block = await SeedBlockAsync(doc.Id, "paragraph", """{"text":"Here."}""", 0);
        var elsewhere = await SeedBlockAsync(other.Id, "paragraph", """{"text":"There."}""", 0);
        var app = App("mcpc_ids");
        var otherComment = (await CallAsync(app, "add_comment", new { documentId = other.Id.ToString(), blockId = elsewhere.Id.ToString(), text = "On the other one." }))
            .GetProperty("id").GetGuid();

        (await CallAsync(app, "add_comment", new { documentId = doc.Id.ToString(), blockId = elsewhere.Id.ToString(), text = "Wrong block." }))
            .GetProperty("error").GetString().Should().Contain("No block");
        (await CallAsync(app, "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "   " }))
            .GetProperty("category").GetString().Should().Be("invalid");
        foreach (var wrong in new[] { Guid.NewGuid().ToString(), "nope", otherComment.ToString() })
        {
            (await CallAsync(app, "reply_to_comment", new { documentId = doc.Id.ToString(), commentId = wrong, text = "Hello?" }))
                .GetProperty("error").GetString().Should().Contain("No comment", wrong);
            (await CallAsync(app, "resolve_comment", new { documentId = doc.Id.ToString(), commentId = wrong }))
                .GetProperty("error").GetString().Should().Contain("No comment", wrong);
        }

        var stranger = App("mcpc_stranger");
        (await CallAsync(stranger, "list_comments", new { documentId = other.Id.ToString() })).GetProperty("error").GetString().Should().Contain("No document");
        (await CallAsync(stranger, "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "Let me in." }))
            .GetProperty("error").GetString().Should().Contain("No document");
        (await CallAsync(stranger, "resolve_comment", new { documentId = other.Id.ToString(), commentId = otherComment.ToString() }))
            .GetProperty("error").GetString().Should().Contain("No document");

        (await CallAsync(App("mcpc_ids", "openid documents:read"), "add_comment", new { documentId = doc.Id.ToString(), blockId = block.Id.ToString(), text = "Read-only app." }))
            .GetProperty("error").GetString().Should().Contain("connect again");

        await using var db = CreateDbContext();
        (await db.Comments.CountAsync(c => c.DocumentId == doc.Id)).Should().Be(0, "nothing was written on the first document");
        (await db.Comments.SingleAsync(c => c.Id == otherComment)).Resolved.Should().BeFalse();
    }
}
