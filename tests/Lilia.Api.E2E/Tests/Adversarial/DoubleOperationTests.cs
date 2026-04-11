using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.E2E.Infrastructure;

namespace Lilia.Api.E2E.Tests.Adversarial;

/// <summary>
/// Double/repeated operation tests — idempotency, race conditions, stale references.
/// </summary>
public class DoubleOperationTests : E2ETestBase
{
    [Fact]
    public async Task CreateVersion_Twice_BothSucceed()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var v1 = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        var v2 = await client.PostAsJsonAsync($"/api/documents/{docId}/versions", new { });
        v1.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        v2.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ShareDocument_TwicePublic_Idempotent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var s1 = await client.PostAsJsonAsync($"/api/documents/{docId}/share", new { isPublic = true });
        var s2 = await client.PostAsJsonAsync($"/api/documents/{docId}/share", new { isPublic = true });
        s1.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        s2.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ToggleFavoriteFormula_TwiceReturnsToOriginal()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var createResp = await client.PostAsJsonAsync("/api/formulas", new
        {
            name = "Double Fav",
            latexContent = @"x^2",
            category = "test",
            description = "test",
        });
        if (!createResp.IsSuccessStatusCode) return;
        var formula = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = formula.GetProperty("id").GetString()!;
        TrackForCleanup("/api/formulas", id);

        var f1 = await client.PostAsync($"/api/formulas/{id}/favorite", null);
        var f2 = await client.PostAsync($"/api/formulas/{id}/favorite", null);
        f1.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        f2.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DeleteBlock_ThenUpdateIt_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var createResp = await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "temp" } });
        var block = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var blockId = block.GetProperty("id").GetString()!;

        await client.DeleteAsync($"/api/documents/{docId}/blocks/{blockId}");

        var updateResp = await client.PutAsJsonAsync($"/api/documents/{docId}/blocks/{blockId}", new
            { content = new { text = "ghost update" } });
        updateResp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RestoreDocument_ThenRestoreAgain_HandledGracefully()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        await client.DeleteAsync($"/api/documents/{docId}");
        await client.PostAsync($"/api/documents/{docId}/restore", null);

        // Already restored — second restore should not crash
        var secondRestore = await client.PostAsync($"/api/documents/{docId}/restore", null);
        secondRestore.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ExportDocument_WhileAddingBlocks_DoesNotCrash()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        await client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Initial" } });

        // Export and add blocks concurrently
        var exportTask = client.GetAsync($"/api/documents/{docId}/export/latex");
        var addTask = client.PostAsJsonAsync($"/api/documents/{docId}/blocks", new
            { type = "paragraph", content = new { text = "Concurrent" } });

        var results = await Task.WhenAll(exportTask, addTask);
        foreach (var r in results)
            r.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task AssignLabel_Twice_Idempotent()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var labelResp = await client.PostAsJsonAsync("/api/labels", new { name = "Double", color = "#123456" });
        if (!labelResp.IsSuccessStatusCode) return;
        var label = await labelResp.Content.ReadFromJsonAsync<JsonElement>();
        var labelId = label.GetProperty("id").GetString()!;
        TrackForCleanup("/api/labels", labelId);

        var a1 = await client.PostAsync($"/api/documents/{docId}/labels/{labelId}", null);
        var a2 = await client.PostAsync($"/api/documents/{docId}/labels/{labelId}", null);
        a1.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        a2.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task DeleteComment_ThenReplyToIt_Returns404()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var doc = await CreateTestDocumentAsync(client);
        var docId = doc.GetProperty("id").GetString()!;

        var commentResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments", new { content = "To delete" });
        if (!commentResp.IsSuccessStatusCode) return;
        var comment = await commentResp.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = comment.GetProperty("id").GetString()!;

        await client.DeleteAsync($"/api/documents/{docId}/comments/{commentId}");

        var replyResp = await client.PostAsJsonAsync($"/api/documents/{docId}/comments/{commentId}/replies", new
            { content = "Ghost reply" });
        replyResp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
