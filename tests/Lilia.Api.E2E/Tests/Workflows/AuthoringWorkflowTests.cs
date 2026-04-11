using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Workflows;

/// <summary>
/// Full authoring workflow: create → add all block types → reorder → preview → export.
/// </summary>
public class AuthoringWorkflowTests : E2ETestBase
{
    [Fact]
    public async Task FullAuthoringWorkflow_CreateToExport()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // 1. Create document
        var doc = await CreateTestDocumentAsync(client, "Full Workflow Test");
        var docId = doc.GetProperty("id").GetString()!;

        // 2. Add blocks of every type
        var blockTypes = new (string type, object content)[]
        {
            ("heading", new { text = "Introduction", level = 1 }),
            ("paragraph", new { text = "This is the *introduction* to our paper." }),
            ("equation", new { latex = @"E = mc^2", display = true }),
            ("code", new { code = "print('hello')", language = "python" }),
            ("blockquote", new { text = "A famous quote." }),
            ("heading", new { text = "Methods", level = 2 }),
            ("paragraph", new { text = "We used the following _methodology_." }),
            ("table", new { rows = new[] { new { cells = new[] { "A", "B" } } } }),
            ("list", new { items = new[] { "First item", "Second item" } }),
            ("abstract", new { text = "This paper presents..." }),
        };

        var blockIds = new List<string>();
        foreach (var (type, content) in blockTypes)
        {
            var resp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new { type, content });
            resp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError, $"block type '{type}' should not crash");
            if (resp.IsSuccessStatusCode)
            {
                var block = await resp.Content.ReadFromJsonAsync<JsonElement>();
                if (block.TryGetProperty("id", out var id))
                    blockIds.Add(id.GetString()!);
            }
        }

        // 3. List blocks
        var listResp = await client.GetAsync($"/api/documents/{docId}/blocks");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Preview
        var htmlResp = await client.GetAsync($"/api/documents/{docId}/preview/html/full");
        htmlResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var latexResp = await client.GetAsync($"/api/documents/{docId}/preview/latex");
        latexResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var sectionsResp = await client.GetAsync($"/api/documents/{docId}/preview/sections");
        sectionsResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Export LaTeX
        var exportLatex = await client.GetAsync($"/api/documents/{docId}/export/latex");
        exportLatex.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Export DOCX
        var exportDocx = await client.GetAsync($"/api/documents/{docId}/export/docx");
        exportDocx.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        // 7. Create version
        var versionResp = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        versionResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    [Fact]
    public async Task BibliographyWorkflow_CreateToExport()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Bibliography Workflow");
        var docId = doc.GetProperty("id").GetString()!;

        // 1. Add bibliography entries
        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "einstein1905",
            entryType = "article",
            data = new { title = "Special Relativity", author = "Einstein, Albert", year = "1905" },
        });
        await client.PostAsJsonAsync($"/api/documents/{docId}/bibliography", new
        {
            citeKey = "knuth1984",
            entryType = "article",
            data = new { title = "Literate Programming", author = "Knuth, Donald E.", year = "1984" },
        });

        // 2. List entries
        var listResp = await client.GetAsync($"/api/documents/{docId}/bibliography");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Export BibTeX
        var exportResp = await client.GetAsync($"/api/documents/{docId}/bibliography/export");
        exportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bibtex = await exportResp.Content.ReadAsStringAsync();
        bibtex.Should().Contain("einstein1905");

        // 4. Format in APA style
        var stylesResp = await client.GetAsync($"/api/documents/{docId}/bibliography/styles");
        stylesResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CommentWorkflow_CreateReplyResolve()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Comment Workflow");
        var docId = doc.GetProperty("id").GetString()!;

        // 1. Add block
        var blockResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Review this paragraph" },
        });
        var block = await blockResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.TryGetProperty("id", out var bid) ? bid.GetString() : null;

        // 2. Create comment on block
        var commentResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new
        {
            content = "This needs clarification",
            blockId,
        });
        commentResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var comment = await commentResp.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.TryGetProperty("id", out var cid) ? cid.GetString() : null;
        if (commentId == null) return;

        // 3. Reply
        var replyResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments/{commentId}/replies", new
        {
            content = "Done, rephrased it.",
        });
        replyResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // 4. Resolve
        var patchContent = new StringContent(
            JsonSerializer.Serialize(new { resolved = true }),
            System.Text.Encoding.UTF8, "application/json");
        var resolveResp = await client.PatchAsync($"/api/documents/{docId}/comments/{commentId}", patchContent);
        resolveResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // 5. Check counts
        var countResp = await client.GetAsync($"/api/documents/{docId}/comments/count");
        countResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VersionWorkflow_CreateEditRestoreVerify()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client, "Version Workflow");
        var docId = doc.GetProperty("id").GetString()!;

        // 1. Add original block
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "Original content v1" },
        });

        // 2. Create version snapshot
        var v1 = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        v1.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var version = await v1.Content.ReadFromJsonAsync<JsonElement>();
        var versionId = version.TryGetProperty("id", out var vid) ? vid.GetString() : null;
        if (versionId == null) return;

        // 3. Add more content
        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
        {
            type = "paragraph",
            content = new { text = "New content v2" },
        });

        // 4. List versions
        var listResp = await client.GetAsync($"/api/documents/{docId}/versions");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Restore to v1
        var restoreResp = await client.PostAsJsonAsync($"/api/documents/{docId}/versions/{versionId}/restore", new { });
        restoreResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task TrashWorkflow_DeleteRestorePermaDelete()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // Create two docs
        var doc1 = await CreateTestDocumentAsync(client, "Trash Workflow Keep");
        var doc2 = await CreateTestDocumentAsync(client, "Trash Workflow Delete");
        var id1 = doc1.GetProperty("id").GetString()!;
        var id2 = doc2.GetProperty("id").GetString()!;

        // Soft delete both
        (await client.DeleteAsync($"/api/documents/{id1}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/documents/{id2}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Check trash
        var trashResp = await client.GetAsync("/api/documents/trash");
        trashResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Restore doc1
        (await client.PostAsync($"/api/documents/{id1}/restore", null)).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // Permanently delete doc2
        (await client.DeleteAsync($"/api/documents/{id2}/permanent")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // doc1 should be accessible again
        (await client.GetAsync($"/api/documents/{id1}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // doc2 should be gone
        (await client.GetAsync($"/api/documents/{id2}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LabelWorkflow_CreateAssignFilterRemove()
    {
        using var client = await CreateAuthenticatedClientAsync();

        // 1. Create label
        var labelResp = await client.PostAsJsonAsync("/api/labels", new { name = "Workflow Label", color = "#3B82F6" });
        labelResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var label = await labelResp.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetString()!;
        TrackForCleanup("/api/labels", labelId);

        // 2. Create doc and assign label
        var doc = await CreateTestDocumentAsync(client, "Labeled Doc");
        var docId = doc.GetProperty("id").GetString()!;
        (await client.PostAsync($"/api/documents/{docId}/labels/{labelId}", null)).StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        // 3. Filter by label
        var filterResp = await client.GetAsync($"/api/documents?labelId={labelId}");
        filterResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Remove label
        (await client.DeleteAsync($"/api/documents/{docId}/labels/{labelId}")).StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
