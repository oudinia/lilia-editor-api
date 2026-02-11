using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;

namespace Lilia.Api.Tests.Integration.Controllers;

[Collection("Integration")]
public class ImportReviewControllerTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";
    private const string OtherUserId = "test_user_002";
    private const string ViewerUserId = "test_user_003";
    private const string BaseUrl = "/api/lilia/import-review/sessions";

    public ImportReviewControllerTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task SeedDefaultUsers()
    {
        await SeedUserAsync(UserId, "test@lilia.test", "Test User");
        await SeedUserAsync(OtherUserId, "other@lilia.test", "Other User");
        await SeedUserAsync(ViewerUserId, "viewer@lilia.test", "Viewer User");
    }

    private static object CreateTestSessionPayload(string title = "Test Import", int blockCount = 3)
    {
        var blocks = Enumerable.Range(0, blockCount).Select(i => new
        {
            id = Guid.NewGuid().ToString(),
            type = i == 0 ? "heading" : "paragraph",
            content = i == 0
                ? JsonSerializer.SerializeToElement(new { text = "Chapter 1", level = 1 })
                : JsonSerializer.SerializeToElement(new { text = $"Paragraph {i}" }),
            confidence = i == 0 ? 90 : 95,
            warnings = (JsonElement?)null,
            sortOrder = i,
            depth = 0
        }).ToList();

        return new
        {
            documentTitle = title,
            blocks
        };
    }

    private async Task<Guid> CreateSessionAndReturnId(string title = "Test Import", int blockCount = 3)
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, CreateTestSessionPayload(title, blockCount));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("session").GetProperty("id").GetGuid();
    }

    private async Task<(Guid sessionId, List<string> blockIds)> CreateSessionWithBlocks(string title = "Test Import", int blockCount = 3)
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, CreateTestSessionPayload(title, blockCount));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = result.GetProperty("session").GetProperty("id").GetGuid();
        var blockIds = result.GetProperty("blocks").EnumerateArray()
            .Select(b => b.GetProperty("blockId").GetString()!)
            .ToList();
        return (sessionId, blockIds);
    }

    // =====================================================
    // POST /api/lilia/import-review/sessions — Create Session
    // =====================================================

    [Fact]
    public async Task CreateSession_ReturnsSessionWithBlocks()
    {
        await SeedDefaultUsers();

        var response = await Client.PostAsJsonAsync(BaseUrl, CreateTestSessionPayload("My Import", 4));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        result.GetProperty("session").GetProperty("documentTitle").GetString().Should().Be("My Import");
        result.GetProperty("session").GetProperty("status").GetString().Should().Be("in_progress");
        result.GetProperty("session").GetProperty("ownerId").GetString().Should().Be(UserId);
        result.GetProperty("blocks").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task CreateSession_BlocksHaveCorrectStatus()
    {
        await SeedDefaultUsers();

        var response = await Client.PostAsJsonAsync(BaseUrl, CreateTestSessionPayload());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var block in result.GetProperty("blocks").EnumerateArray())
        {
            block.GetProperty("status").GetString().Should().Be("pending");
        }
    }

    [Fact]
    public async Task CreateSession_Returns401_WhenAnonymous()
    {
        using var anonClient = CreateAnonymousClient();
        var response = await anonClient.PostAsJsonAsync(BaseUrl, CreateTestSessionPayload());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =====================================================
    // GET /api/lilia/import-review/sessions/{id} — Load Session
    // =====================================================

    [Fact]
    public async Task GetSession_ReturnsFullSessionData()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId("My Import");

        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        result.GetProperty("session").GetProperty("id").GetGuid().Should().Be(sessionId);
        result.GetProperty("owner").GetProperty("id").GetString().Should().Be(UserId);
        result.GetProperty("blocks").GetArrayLength().Should().Be(3);
        result.GetProperty("collaborators").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        result.GetProperty("userRole").GetString().Should().Be("owner");
    }

    [Fact]
    public async Task GetSession_Returns404_WhenNotExist()
    {
        await SeedDefaultUsers();

        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSession_Returns404_WhenNotCollaborator()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        using var otherClient = CreateClientAs(OtherUserId);
        var response = await otherClient.GetAsync($"{BaseUrl}/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================
    // DELETE /api/lilia/import-review/sessions/{id} — Cancel
    // =====================================================

    [Fact]
    public async Task CancelSession_Returns204_WhenOwner()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        var response = await Client.DeleteAsync($"{BaseUrl}/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify cancelled
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}");
        var data = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        data.GetProperty("session").GetProperty("status").GetString().Should().Be("cancelled");
    }

    [Fact]
    public async Task CancelSession_Permanent_DeletesAllData()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        var response = await Client.DeleteAsync($"{BaseUrl}/{sessionId}?permanent=true");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Session should be gone
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CancelSession_Returns404_WhenNotOwner()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        using var otherClient = CreateClientAs(OtherUserId);
        var response = await otherClient.DeleteAsync($"{BaseUrl}/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================
    // PATCH /api/lilia/import-review/sessions/{id}/blocks/{blockId}
    // =====================================================

    [Fact]
    public async Task UpdateBlock_ApprovesBlock()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().Should().Be("approved");
        result.GetProperty("reviewedBy").GetString().Should().Be(UserId);
    }

    [Fact]
    public async Task UpdateBlock_EditsContent()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        var newContent = JsonSerializer.SerializeToElement(new { text = "Updated text" });
        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[1]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[1], currentContent = newContent, currentType = "heading" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().Should().Be("edited");
        result.GetProperty("currentType").GetString().Should().Be("heading");
    }

    [Fact]
    public async Task UpdateBlock_Returns404_WhenViewerRole()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add viewer collaborator
        await Client.PostAsJsonAsync($"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "viewer@lilia.test", role = "viewer" });

        using var viewerClient = CreateClientAs(ViewerUserId);
        var response = await viewerClient.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================
    // POST /api/lilia/import-review/sessions/{id}/blocks/{blockId}/reset
    // =====================================================

    [Fact]
    public async Task ResetBlock_RestoresToPending()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // First approve
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        // Then reset
        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}/reset",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0] });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().Should().Be("pending");
        // reviewedBy is null and may be omitted by WhenWritingNull serialization
        result.TryGetProperty("reviewedBy", out var reviewedBy).Should().BeFalse("reviewedBy should be null/omitted after reset");
    }

    // =====================================================
    // POST /api/lilia/import-review/sessions/{id}/bulk-action
    // =====================================================

    [Fact]
    public async Task BulkAction_ApproveAll_ApprovesAllPendingBlocks()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks("Bulk Test", 5);

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("affected").GetInt32().Should().Be(5);

        // Verify all blocks are approved
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}");
        var sessionData = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var block in sessionData.GetProperty("blocks").EnumerateArray())
        {
            block.GetProperty("status").GetString().Should().Be("approved");
        }
    }

    [Fact]
    public async Task BulkAction_ResetAll_ResetsAllBlocks()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks("Reset Test", 3);

        // Approve all first
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        // Reset all
        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "resetAll" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("affected").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task BulkAction_ApproveSelected_OnlyApprovesSpecifiedBlocks()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks("Selected Test", 4);

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveSelected", blockIds = new[] { blockIds[0], blockIds[2] } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("affected").GetInt32().Should().Be(2);
    }

    // =====================================================
    // POST /api/lilia/import-review/sessions/{id}/finalize
    // =====================================================

    [Fact]
    public async Task FinalizeSession_CreatesDocument_WhenAllBlocksReviewed()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks("Finalize Test", 3);

        // Approve all blocks
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { documentTitle = "My Finalized Document" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("document").GetProperty("title").GetString().Should().Be("My Finalized Document");
        result.GetProperty("statistics").GetProperty("importedBlocks").GetInt32().Should().Be(3);
        result.GetProperty("statistics").GetProperty("skippedBlocks").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task FinalizeSession_SkipsRejectedBlocks()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks("Skip Test", 3);

        // Approve 2, reject 1
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[1]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[1], status = "rejected" });
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[2]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[2], status = "approved" });

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("statistics").GetProperty("importedBlocks").GetInt32().Should().Be(2);
        result.GetProperty("statistics").GetProperty("skippedBlocks").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task FinalizeSession_Returns400_WhenPendingBlocks_WithoutForce()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks("Pending Test", 3);

        // Only approve 1 of 3
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { force = false });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FinalizeSession_Succeeds_WhenPendingBlocks_WithForce()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks("Force Test", 3);

        // Only approve 1 of 3
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { force = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("statistics").GetProperty("importedBlocks").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task FinalizeSession_SetsSessionStatusToImported()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks("Status Test", 2);

        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { });

        // Check session status
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}");
        var sessionData = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        sessionData.GetProperty("session").GetProperty("status").GetString().Should().Be("imported");
        sessionData.GetProperty("session").GetProperty("documentId").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    // =====================================================
    // Collaborators
    // =====================================================

    [Fact]
    public async Task AddCollaborator_ReturnsCollaboratorInfo()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "other@lilia.test", role = "reviewer" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("collaborator").GetProperty("userId").GetString().Should().Be(OtherUserId);
        result.GetProperty("collaborator").GetProperty("role").GetString().Should().Be("reviewer");
    }

    [Fact]
    public async Task AddCollaborator_AllowsReviewerAccess()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        // Add collaborator
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "other@lilia.test", role = "reviewer" });

        // Reviewer can access session
        using var otherClient = CreateClientAs(OtherUserId);
        var response = await otherClient.GetAsync($"{BaseUrl}/{sessionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        data.GetProperty("userRole").GetString().Should().Be("reviewer");
    }

    [Fact]
    public async Task RemoveCollaborator_Returns204()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        // Add then remove
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "other@lilia.test", role = "reviewer" });

        var response = await Client.DeleteAsync($"{BaseUrl}/{sessionId}/collaborators/{OtherUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify access revoked
        using var otherClient = CreateClientAs(OtherUserId);
        var getResponse = await otherClient.GetAsync($"{BaseUrl}/{sessionId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RemoveCollaborator_CannotRemoveOwner()
    {
        await SeedDefaultUsers();
        var sessionId = await CreateSessionAndReturnId();

        var response = await Client.DeleteAsync($"{BaseUrl}/{sessionId}/collaborators/{UserId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================
    // Comments
    // =====================================================

    [Fact]
    public async Task AddComment_ReturnsComment()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Is this really a heading?" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("comment").GetProperty("content").GetString().Should().Be("Is this really a heading?");
        result.GetProperty("comment").GetProperty("blockId").GetString().Should().Be(blockIds[0]);
        result.GetProperty("comment").GetProperty("resolved").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetComments_ReturnsAllComments()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add 2 comments
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Comment 1" });
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[1], content = "Comment 2" });

        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}/comments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("comments").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task GetComments_FiltersByBlockId()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Block 0 comment" });
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[1], content = "Block 1 comment" });

        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}/comments?blockId={blockIds[0]}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("comments").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ResolveComment_SetsResolvedTrue()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add comment
        var addResponse = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Fix this" });
        var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = addResult.GetProperty("comment").GetProperty("id").GetGuid();

        // Resolve it
        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments/{commentId}",
            new { sessionId = sessionId.ToString(), commentId = commentId.ToString(), resolved = true });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify resolved
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}/comments");
        var comments = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var comment = comments.GetProperty("comments").EnumerateArray().First();
        comment.GetProperty("resolved").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DeleteComment_RemovesComment()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add comment
        var addResponse = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "To delete" });
        var addResult = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var commentId = addResult.GetProperty("comment").GetProperty("id").GetGuid();

        // Delete
        var response = await Client.DeleteAsync($"{BaseUrl}/{sessionId}/comments/{commentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify gone
        var getResponse = await Client.GetAsync($"{BaseUrl}/{sessionId}/comments");
        var comments = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        comments.GetProperty("comments").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Comments_AffectBlockCommentCounts()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add 2 comments to first block
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Comment A" });
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Comment B" });

        // Load session and check comment counts
        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}");
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        var firstBlock = data.GetProperty("blocks").EnumerateArray().First();
        firstBlock.GetProperty("commentCount").GetInt32().Should().Be(2);
        firstBlock.GetProperty("unresolvedCommentCount").GetInt32().Should().Be(2);
    }

    // =====================================================
    // Activity
    // =====================================================

    [Fact]
    public async Task GetActivities_ReturnsActivityLog()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Perform some actions to generate activities
        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}/activity?limit=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Should have at least session_created and block_updated
        result.GetProperty("activities").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetRecentActivities_ReturnsSinceTimestamp()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        var since = DateTime.UtcNow;
        // Small delay to ensure activities are after `since`
        await Task.Delay(100);

        await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        var response = await Client.GetAsync($"{BaseUrl}/{sessionId}/activity/recent?since={since:O}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("activities").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }

    // =====================================================
    // Access Control: Reviewer can write, Viewer cannot
    // =====================================================

    [Fact]
    public async Task Reviewer_CanApproveBlocks()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add reviewer
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "other@lilia.test", role = "reviewer" });

        // Reviewer approves block
        using var otherClient = CreateClientAs(OtherUserId);
        var response = await otherClient.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[0]}",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], status = "approved" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CanAddComments()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        // Add viewer
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "viewer@lilia.test", role = "viewer" });

        // Viewer adds comment (allowed for all roles)
        using var viewerClient = CreateClientAs(ViewerUserId);
        var response = await viewerClient.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/comments",
            new { sessionId = sessionId.ToString(), blockId = blockIds[0], content = "Looks good!" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Viewer_CannotFinalize()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks();

        // Add viewer and approve all blocks as owner first
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/collaborators",
            new { sessionId = sessionId.ToString(), email = "viewer@lilia.test", role = "viewer" });
        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        // Viewer tries to finalize
        using var viewerClient = CreateClientAs(ViewerUserId);
        var response = await viewerClient.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================
    // Edge Cases
    // =====================================================

    [Fact]
    public async Task FinalizeSession_CannotFinalizeTwice()
    {
        await SeedDefaultUsers();
        var (sessionId, _) = await CreateSessionWithBlocks("Double Finalize", 2);

        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/bulk-action",
            new { sessionId = sessionId.ToString(), action = "approveAll" });

        // First finalize succeeds
        var response1 = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize", new { });
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second finalize fails (status is now "imported", not "in_progress")
        var response2 = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{sessionId}/finalize", new { });
        response2.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateBlock_WithContentAndType_MarksAsEdited()
    {
        await SeedDefaultUsers();
        var (sessionId, blockIds) = await CreateSessionWithBlocks();

        var newContent = JsonSerializer.SerializeToElement(new { text = "Changed to abstract" });
        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{sessionId}/blocks/{blockIds[1]}",
            new
            {
                sessionId = sessionId.ToString(),
                blockId = blockIds[1],
                currentContent = newContent,
                currentType = "abstract"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("status").GetString().Should().Be("edited");
        result.GetProperty("currentType").GetString().Should().Be("abstract");
    }
}
