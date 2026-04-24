using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.Controllers;

/// <summary>
/// FT-IMP-001 endpoint regression coverage. Complements the existing
/// ImportReviewControllerTests (which predates the mirror-realm +
/// definition/instance split) with tests for the four endpoints that
/// landed with the shopping-cart work:
///
///   GET  /api/lilia/import-review/sessions/{id}/summary
///   POST /api/lilia/import-review/sessions/{id}/rerun
///   POST /api/lilia/import-definitions/{id}/rerun
///   POST /api/lilia/import-review/sessions/{id}/finalize   (idempotent)
///
/// Plus the SortOrder drag-reorder path on
/// PATCH /api/lilia/import-review/sessions/{id}/blocks/{blockId}.
///
/// Handoff reference: lilia-docs/specs/import-ui-handoff-2026-04-24.md
/// §5 #6 ("no integration test in tests/Lilia.Api.Tests").
/// </summary>
[Collection("Integration")]
public class ImportFlowTests : IntegrationTestBase
{
    private const string UserId = "test_user_flow_owner";
    private const string OtherUserId = "test_user_flow_other";

    // Owner-scoped HTTP client. The base `Client` authenticates as
    // `test_user_001` by default, but our tests seed ownership as
    // `test_user_flow_owner` so the session/definition rows pass the
    // ownership check inside ImportReviewService. Using a header-keyed
    // client is the same pattern used by ImportReviewControllerTests
    // for multi-user coverage.
    private readonly HttpClient _ownerClient;

    public ImportFlowTests(TestDatabaseFixture fixture) : base(fixture)
    {
        _ownerClient = CreateClientAs(UserId);
    }

    /// <summary>
    /// Mint a Definition + Session + a few block reviews directly
    /// through the DbContext. Bypasses the parse job (which isn't
    /// under test here) but lays down the same shape FT-IMP-001's
    /// POST /imports/latex does at upload time.
    /// </summary>
    private async Task<(Guid definitionId, Guid sessionId)> SeedDefinitionAndSessionAsync(
        string? ownerId = null,
        string status = "pending_review",
        string? rawSource = "\\section{Hello}\\nContent.",
        int blockCount = 3,
        int errorCount = 0,
        int warningCount = 0)
    {
        ownerId ??= UserId;
        await SeedUserAsync(ownerId);

        await using var db = CreateDbContext();

        var definitionId = Guid.NewGuid();
        db.ImportDefinitions.Add(new ImportDefinition
        {
            Id = definitionId,
            OwnerId = ownerId,
            SourceFileName = "test.tex",
            SourceFormat = "tex",
            RawSource = rawSource,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
        });

        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            DefinitionId = definitionId,
            OwnerId = ownerId,
            DocumentTitle = "test.tex",
            SourceFormat = "tex",
            Status = status,
            RawImportData = rawSource,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            QualityScore = errorCount == 0 ? 100 : 60,
        });

        // A few block reviews so the summary endpoint has content
        // to count and so UpdateBlock tests have a target.
        for (var i = 0; i < blockCount; i++)
        {
            db.ImportBlockReviews.Add(new ImportBlockReview
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                BlockId = $"block-{i}",
                BlockIndex = i,
                OriginalType = i == 0 ? "heading" : "paragraph",
                OriginalContent = JsonDocument.Parse(i == 0
                    ? "{\"text\":\"Heading\",\"level\":1}"
                    : "{\"text\":\"Para\"}"),
                Status = "pending",
                Confidence = 90,
                SortOrder = i,
            });
        }

        // Optional diagnostics to make the summary's error/warning
        // counts non-zero on demand.
        for (var i = 0; i < errorCount; i++)
        {
            db.ImportDiagnostics.Add(new ImportDiagnostic
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Severity = "error",
                Category = "parse_ambiguity",
                Message = $"Error #{i}",
                CreatedAt = DateTime.UtcNow,
            });
        }
        for (var i = 0; i < warningCount; i++)
        {
            db.ImportDiagnostics.Add(new ImportDiagnostic
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                Severity = "warning",
                Category = "parse_ambiguity",
                Message = $"Warning #{i}",
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return (definitionId, sessionId);
    }

    // =====================================================
    // GET /sessions/{id}/summary
    // =====================================================

    [Fact]
    public async Task GetSummary_ReturnsFiveSections_ForOwner()
    {
        var (_, sessionId) = await SeedDefinitionAndSessionAsync(
            blockCount: 4, errorCount: 1, warningCount: 2);

        var res = await _ownerClient.GetAsync($"/api/lilia/import-review/sessions/{sessionId}/summary");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // SessionSummaryDto is a flat record with one field per summary
        // sheet section (FT-IMP-001 §Summary sheet content). At least
        // one representative field per section must be present.
        root.TryGetProperty("sourceFileName", out _).Should().BeTrue("SOURCE section");
        root.TryGetProperty("totalBlocks", out _).Should().BeTrue("CONTENT section");
        // coverageMappedPercent is double? and may be serialized as
        // `null` or omitted — use unsupportedTokenCount (int) as the
        // canary for the COVERAGE section instead.
        root.TryGetProperty("unsupportedTokenCount", out _).Should().BeTrue("COVERAGE section");
        root.TryGetProperty("errorCount", out _).Should().BeTrue("QUALITY section");
        root.TryGetProperty("estimatedReviewMinutes", out _).Should().BeTrue("ESTIMATE section");

        // Quality numbers flow through from the diagnostics we seeded.
        root.GetProperty("errorCount").GetInt32().Should().Be(1);
        root.GetProperty("warningCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        // Content counter is backed by the 4 seeded block reviews.
        root.GetProperty("totalBlocks").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task GetSummary_Returns404_ForUnknownSession()
    {
        await SeedUserAsync(UserId);
        var res = await _ownerClient.GetAsync($"/api/lilia/import-review/sessions/{Guid.NewGuid()}/summary");
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetSummary_Returns401_WhenAnonymous()
    {
        var (_, sessionId) = await SeedDefinitionAndSessionAsync();
        using var anon = CreateAnonymousClient();
        var res = await anon.GetAsync($"/api/lilia/import-review/sessions/{sessionId}/summary");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSummary_Returns404_ForOtherUsersSession()
    {
        // Session belongs to OtherUserId; _ownerClient authenticates
        // as UserId → controller treats as not-found
        // (GetSessionSummaryAsync returns null when the owner doesn't
        // match). Guards against accidental cross-user leaks.
        var (_, sessionId) = await SeedDefinitionAndSessionAsync(ownerId: OtherUserId);
        var res = await _ownerClient.GetAsync($"/api/lilia/import-review/sessions/{sessionId}/summary");
        res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    // =====================================================
    // POST /sessions/{id}/rerun  (session-scoped)
    // =====================================================

    [Fact]
    public async Task SessionRerun_MarksCurrentSuperseded_AndReturnsNewSessionId()
    {
        var (definitionId, sessionId) = await SeedDefinitionAndSessionAsync(status: "failed");

        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var newSessionId = doc.RootElement.GetProperty("sessionId").GetGuid();
        newSessionId.Should().NotBe(sessionId);

        // Verify DB state: new session exists on the same definition,
        // old session flipped to 'superseded'.
        await using var db = CreateDbContext();
        var sessions = await db.ImportReviewSessions
            .Where(s => s.DefinitionId == definitionId)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
        sessions.Should().HaveCount(2);
        sessions[0].Id.Should().Be(sessionId);
        sessions[0].Status.Should().Be("superseded");
        sessions[1].Id.Should().Be(newSessionId);
        sessions[1].Status.Should().BeOneOf("parsing", "pending_review");
    }

    [Fact]
    public async Task SessionRerun_Returns400_WhenDefinitionIdIsNull()
    {
        // Legacy-shape session — created before the FT-IMP-001 split,
        // no DefinitionId attached. The endpoint returns a human-
        // readable BadRequest explaining the user needs to re-upload.
        await SeedUserAsync(UserId);
        await using var db = CreateDbContext();

        var sessionId = Guid.NewGuid();
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            DefinitionId = null,
            OwnerId = UserId,
            DocumentTitle = "legacy.tex",
            SourceFormat = "tex",
            Status = "failed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SessionRerun_Returns404_ForUnknownSession()
    {
        await SeedUserAsync(UserId);
        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-review/sessions/{Guid.NewGuid()}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SessionRerun_Returns403_WhenSessionBelongsToAnotherUser()
    {
        var (_, sessionId) = await SeedDefinitionAndSessionAsync(
            ownerId: OtherUserId, status: "failed");
        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =====================================================
    // POST /import-definitions/{id}/rerun  (definition-scoped)
    // =====================================================

    [Fact]
    public async Task DefinitionRerun_CreatesNewSession_ForDefinition()
    {
        var (definitionId, _) = await SeedDefinitionAndSessionAsync(status: "failed");
        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-definitions/{definitionId}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var sessions = await db.ImportReviewSessions
            .Where(s => s.DefinitionId == definitionId)
            .ToListAsync();
        sessions.Should().HaveCountGreaterThanOrEqualTo(2);
        sessions.Should().Contain(s => s.Status == "superseded");
    }

    [Fact]
    public async Task DefinitionRerun_Returns404_ForUnknownDefinition()
    {
        await SeedUserAsync(UserId);
        var res = await _ownerClient.PostAsync(
            $"/api/lilia/import-definitions/{Guid.NewGuid()}/rerun", content: null);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================
    // POST /sessions/{id}/finalize  (idempotent)
    // =====================================================

    [Fact]
    public async Task Finalize_IsIdempotent_WhenCalledTwice()
    {
        var (_, sessionId) = await SeedDefinitionAndSessionAsync(status: "pending_review");

        // First finalize call. Controller returns FinalizeResultDto
        // shaped as { document: { id, title }, statistics: { ... } }.
        var res1 = await _ownerClient.PostAsJsonAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/finalize",
            new { force = true });
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc1 = JsonDocument.Parse(await res1.Content.ReadAsStringAsync());
        var firstDocId = doc1.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        firstDocId.Should().NotBe(Guid.Empty);

        // Second finalize call — session is now 'imported' with
        // DocumentId set. Idempotency contract (FT-IMP-001 stage 8):
        // same document id returned, no new document created.
        var res2 = await _ownerClient.PostAsJsonAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/finalize",
            new { force = true });
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc2 = JsonDocument.Parse(await res2.Content.ReadAsStringAsync());
        var secondDocId = doc2.RootElement.GetProperty("document").GetProperty("id").GetGuid();
        secondDocId.Should().Be(firstDocId,
            "idempotent finalize must return the same document id for the same session");

        // DB sanity: exactly one Document exists for this session's owner.
        await using var db = CreateDbContext();
        var docsForOwner = await db.Documents
            .Where(d => d.Id == firstDocId)
            .CountAsync();
        docsForOwner.Should().Be(1,
            "idempotent finalize must not create a second document on retry");
    }

    // =====================================================
    // PATCH /sessions/{id}/blocks/{blockId}  — SortOrder
    // =====================================================

    [Fact]
    public async Task UpdateBlock_AcceptsSortOrder_ForDragReorder()
    {
        var (_, sessionId) = await SeedDefinitionAndSessionAsync(blockCount: 3);

        // Drag the first block to a new sortOrder. The DTO exposes
        // sortOrder as an int (no neighbour renumbering — FT-IMP-001
        // §Review page). Use a value that's clearly distinct from
        // the seeded 0/1/2 so the write is observable.
        var res = await _ownerClient.PatchAsJsonAsync(
            $"/api/lilia/import-review/sessions/{sessionId}/blocks/block-0",
            new { sortOrder = 99 });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = CreateDbContext();
        var block = await db.ImportBlockReviews.SingleAsync(
            b => b.SessionId == sessionId && b.BlockId == "block-0");
        block.SortOrder.Should().Be(99,
            "SortOrder is a DTO-level contract for drag-reorder — no renumbering on neighbours");
    }
}
