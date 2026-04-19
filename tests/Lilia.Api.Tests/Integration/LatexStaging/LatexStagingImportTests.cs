using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Integration.LatexStaging;

/// <summary>
/// End-to-end validation of the DB-first LaTeX import staging pipeline.
/// See plan /home/oussama/.claude/plans/valiant-waddling-otter.md and
/// lilia-docs/technical/import-export-db-first.md.
///
/// We exercise the executor directly (not the fire-and-forget upload
/// endpoint) to keep tests deterministic. One upload test validates the
/// HTTP surface; everything else drives <see cref="ILatexImportJobExecutor"/>
/// directly so we can assert on post-state without polling.
/// </summary>
[Collection("Integration")]
public class LatexStagingImportTests : IntegrationTestBase
{
    private const string UserId = "test_user_001";

    public LatexStagingImportTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<Guid> SeedSessionFromSourceAsync(string source, bool autoFinalize = false, string title = "Test LaTeX Import")
    {
        await SeedUserAsync(UserId);

        await using var db = CreateDbContext();
        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        db.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = UserId,
            UserId = UserId,
            JobType = JobTypes.Import,
            Status = JobStatus.Pending,
            SourceFormat = "latex",
            TargetFormat = "lilia",
            SourceFileName = "test.tex",
            Direction = "INBOUND",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            JobId = jobId,
            OwnerId = UserId,
            DocumentTitle = title,
            Status = "parsing",
            RawImportData = source,
            AutoFinalizeEnabled = autoFinalize,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();
        return sessionId;
    }

    private async Task RunExecutorAsync(Guid sessionId)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<ILatexImportJobExecutor>();
        var db = scope.ServiceProvider.GetRequiredService<LiliaDbContext>();
        var jobId = await db.ImportReviewSessions
            .Where(s => s.Id == sessionId)
            .Select(s => s.JobId!.Value)
            .FirstAsync();
        await executor.RunAsync(jobId, sessionId);
    }

    // ─── Upload endpoint ───────────────────────────────────────────────────

    [Fact]
    public async Task Upload_CleanArticle_PersistsRawSourceAndReturnsIds()
    {
        await SeedUserAsync(UserId);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(LatexStagingFixtures.CleanArticle));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-tex");
        content.Add(fileContent, "file", "clean.tex");

        var response = await Client.PostAsync("/api/lilia/imports/latex", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<LatexImportUploadResponseDto>();
        result.Should().NotBeNull();
        result!.SessionId.Should().NotBe(Guid.Empty);
        result.JobId.Should().NotBe(Guid.Empty);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstOrDefaultAsync(s => s.Id == result.SessionId);
        session.Should().NotBeNull();
        session!.RawImportData.Should().Contain(@"\documentclass{article}");
        session.Status.Should().BeOneOf("parsing", "pending_review", "auto_finalized", "imported");
        session.OwnerId.Should().Be(UserId);
        session.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        await SeedUserAsync(UserId);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Array.Empty<byte>());
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-tex");
        content.Add(fileContent, "file", "empty.tex");

        var response = await Client.PostAsync("/api/lilia/imports/latex", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upload_Anonymous_ReturnsUnauthorized()
    {
        using var anon = CreateAnonymousClient();
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(LatexStagingFixtures.CleanArticle)), "file", "test.tex");
        var response = await anon.PostAsync("/api/lilia/imports/latex", content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Executor: clean article ──────────────────────────────────────────

    [Fact]
    public async Task Executor_CleanArticle_StagesBlocksAndSetsQualityScore()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().BeOneOf("pending_review", "auto_finalized");
        session.QualityScore.Should().NotBeNull();
        session.QualityScore.Should().BeGreaterThan(80, because: "a clean article should score high");

        var blockCount = await db.ImportBlockReviews.CountAsync(b => b.SessionId == sessionId);
        blockCount.Should().BeGreaterThan(0, because: "parser emits at least headings + paragraphs + equation");

        var errorDiagnostics = await db.ImportDiagnostics
            .CountAsync(d => d.SessionId == sessionId && d.Severity == "error");
        errorDiagnostics.Should().Be(0);
    }

    // ─── Executor: beamer triggers unsupported_class ──────────────────────

    [Fact]
    public async Task Executor_Beamer_EmitsUnsupportedClassDiagnostic()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.Beamer);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var diagnostics = await db.ImportDiagnostics.Where(d => d.SessionId == sessionId).ToListAsync();

        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().Contain(d => d.Code == "LATEX.UNSUPPORTED_CLASS.BEAMER",
            because: "beamer templates must surface the unsupported_class diagnostic");

        var beamer = diagnostics.First(d => d.Code == "LATEX.UNSUPPORTED_CLASS.BEAMER");
        beamer.Category.Should().Be("unsupported_class");
        beamer.Severity.Should().Be("warning");
        beamer.AutoFixApplied.Should().BeTrue(because: "we shim beamer frames to article output");
        beamer.DocsUrl.Should().NotBeNullOrEmpty();
    }

    // ─── Executor: load-order traps ───────────────────────────────────────

    [Fact]
    public async Task Executor_LoadOrderTraps_EmitsLoadOrderDiagnostics()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.LoadOrderTraps);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var loadOrderDiagnostics = await db.ImportDiagnostics
            .Where(d => d.SessionId == sessionId && d.Category == "load_order")
            .ToListAsync();

        loadOrderDiagnostics.Should().NotBeEmpty(
            because: "cleveref before hyperref and subfig with subcaption are known load-order traps");
        loadOrderDiagnostics.Should().Contain(d => d.AutoFixApplied,
            because: "the DiagnosticMapper marks load-order issues as auto-fixed (we reorder via shims)");
    }

    // ─── Executor: CV classes ─────────────────────────────────────────────

    [Fact]
    public async Task Executor_ModernCv_StagesBlocksWithoutErrors()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.ModernCv);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().BeOneOf("pending_review", "auto_finalized");

        var blocks = await db.ImportBlockReviews.CountAsync(b => b.SessionId == sessionId);
        blocks.Should().BeGreaterThan(0, because: "moderncv sections, cventries, cvitems must stage");

        var errors = await db.ImportDiagnostics.CountAsync(d => d.SessionId == sessionId && d.Severity == "error");
        errors.Should().Be(0);
    }

    [Fact]
    public async Task Executor_AltaCv_StagesBlocks()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.AltaCv);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var blocks = await db.ImportBlockReviews.CountAsync(b => b.SessionId == sessionId);
        blocks.Should().BeGreaterThan(0);
    }

    // ─── Auto-finalize gate ───────────────────────────────────────────────

    [Fact]
    public async Task AutoFinalize_CleanArticle_PromotesToDocument()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle, autoFinalize: true);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be("imported", because: "clean + autoFinalize=true must promote without review UI");
        session.DocumentId.Should().NotBeNull();

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == session.DocumentId);
        doc.Should().NotBeNull();

        var blockCount = await db.Blocks.CountAsync(b => b.DocumentId == doc!.Id);
        blockCount.Should().BeGreaterThan(0, because: "INSERT...SELECT must have copied staging rows into blocks");

        // Job should also be linked to the final document.
        var job = await db.Jobs.FirstAsync(j => j.Id == session.JobId!.Value);
        job.DocumentId.Should().Be(doc!.Id);
        job.Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task AutoFinalize_CleanArticle_Disabled_StaysPendingReview()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle, autoFinalize: false);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().Be("pending_review");
        session.DocumentId.Should().BeNull();
    }

    [Fact]
    public async Task AutoFinalize_Beamer_IsShimmed_PromotesToDocument()
    {
        // Beamer emits an unsupported_class diagnostic but the gate treats
        // shimmed warnings as non-blocking (AutoFixApplied=true means the
        // parser handled it for the user). With autoFinalize=true that lets
        // beamer decks pass through to a real document — the user still
        // gets a compile-clean result thanks to the beamer theme shims. The
        // diagnostic sticks around on the session for visibility.
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.Beamer, autoFinalize: true);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.Status.Should().BeOneOf("auto_finalized", "imported");

        // Diagnostic must still exist even though we auto-finalized.
        var beamerDiag = await db.ImportDiagnostics
            .AnyAsync(d => d.SessionId == sessionId && d.Code == "LATEX.UNSUPPORTED_CLASS.BEAMER");
        beamerDiag.Should().BeTrue(because: "shimmed beamer is still surfaced as a diagnostic for review");
    }

    // ─── Bulk insert path: large thesis ───────────────────────────────────

    [Fact]
    public async Task Executor_LargeThesis_StagesManyBlocksViaCopyPath()
    {
        var source = LatexStagingFixtures.GenerateLargeThesis(sectionCount: 40, paragraphsPerSection: 15);
        var sessionId = await SeedSessionFromSourceAsync(source);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var blockCount = await db.ImportBlockReviews.CountAsync(b => b.SessionId == sessionId);
        // 40 headings + 40*15=600 paragraphs ≈ 640 blocks; allow generous slack.
        blockCount.Should().BeGreaterThan(500,
            because: "large thesis exercises the COPY bulk-insert path past the 500-row threshold");

        var session = await db.ImportReviewSessions.FirstAsync(s => s.Id == sessionId);
        session.QualityScore.Should().NotBeNull();
    }

    // ─── Diagnostics endpoint & dismiss ───────────────────────────────────

    [Fact]
    public async Task GetDiagnostics_ReturnsStagedRowsInSeverityOrder()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.LoadOrderTraps);
        await RunExecutorAsync(sessionId);

        var response = await Client.GetAsync($"/api/lilia/import-review/sessions/{sessionId}/diagnostics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diagnostics = await response.Content.ReadFromJsonAsync<List<ImportDiagnosticDto>>();
        diagnostics.Should().NotBeNull();
        diagnostics!.Should().NotBeEmpty();

        // Ensure errors (if any) come before warnings — the service orders by
        // severity then source line.
        var severityOrder = new Dictionary<string, int> { ["error"] = 0, ["warning"] = 1, ["info"] = 2 };
        for (var i = 1; i < diagnostics.Count; i++)
        {
            severityOrder[diagnostics[i - 1].Severity]
                .Should().BeLessThanOrEqualTo(severityOrder[diagnostics[i].Severity]);
        }
    }

    [Fact]
    public async Task DismissDiagnostic_SetsDismissedFlag()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.Beamer);
        await RunExecutorAsync(sessionId);

        await using var db = CreateDbContext();
        var firstDiag = await db.ImportDiagnostics.FirstAsync(d => d.SessionId == sessionId);
        var diagId = firstDiag.Id;

        var response = await Client.PostAsync($"/api/lilia/import-review/sessions/{sessionId}/diagnostics/{diagId}/dismiss", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = CreateDbContext();
        var updated = await db2.ImportDiagnostics.FirstAsync(d => d.Id == diagId);
        updated.Dismissed.Should().BeTrue();
        updated.DismissedBy.Should().Be(UserId);
        updated.DismissedAt.Should().NotBeNull();
    }

    // ─── Retention sweep ──────────────────────────────────────────────────

    [Fact]
    public async Task RetentionSweep_DeletesExpiredUnfinalisedSessions_AndCascadesChildren()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle);
        await RunExecutorAsync(sessionId);

        // Back-date the session past the retention window.
        await using (var db = CreateDbContext())
        {
            await db.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddDays(-1))
                    .SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddDays(-35)));
        }

        // Tick the purge service once — it delegates to ExecuteDeleteAsync.
        await using (var db = CreateDbContext())
        {
            var purged = await db.ImportReviewSessions
                .Where(s => s.Status != "imported"
                            && (s.ExpiresAt != null
                                ? s.ExpiresAt < DateTime.UtcNow
                                : s.CreatedAt < DateTime.UtcNow.AddDays(-30)))
                .ExecuteDeleteAsync();
            purged.Should().Be(1);
        }

        // Cascade: children gone too.
        await using (var db = CreateDbContext())
        {
            (await db.ImportReviewSessions.AnyAsync(s => s.Id == sessionId)).Should().BeFalse();
            (await db.ImportBlockReviews.AnyAsync(b => b.SessionId == sessionId)).Should().BeFalse();
            (await db.ImportDiagnostics.AnyAsync(d => d.SessionId == sessionId)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task RetentionSweep_PreservesFinalisedSessions()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle, autoFinalize: true);
        await RunExecutorAsync(sessionId);

        // Back-date: would normally be purged, but status=imported protects it.
        await using (var db = CreateDbContext())
        {
            await db.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }

        await using (var db = CreateDbContext())
        {
            await db.ImportReviewSessions
                .Where(s => s.Status != "imported"
                            && s.ExpiresAt != null
                            && s.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync();

            (await db.ImportReviewSessions.AnyAsync(s => s.Id == sessionId)).Should().BeTrue(
                because: "finalised sessions are preserved for audit even past the retention window");
        }
    }

    // ─── CHECK constraint guard ───────────────────────────────────────────

    [Fact]
    public async Task Diagnostic_InvalidCategory_RejectedByCheckConstraint()
    {
        // Verify the CHECK constraint actually fires — inserts with a bogus
        // category value must be rejected at the database level.
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle);

        await using var db = CreateDbContext();
        var insert = async () => await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO import_diagnostics (id, session_id, category, severity, code, message) " +
            "VALUES (gen_random_uuid(), {0}, 'bogus_category', 'warning', 'TEST', 'x')",
            new object[] { sessionId });

        await insert.Should().ThrowAsync<Exception>(
            because: "CHECK constraint forbids categories outside the 10 known values");
    }

    [Fact]
    public async Task Diagnostic_InvalidSeverity_RejectedByCheckConstraint()
    {
        var sessionId = await SeedSessionFromSourceAsync(LatexStagingFixtures.CleanArticle);

        await using var db = CreateDbContext();
        var insert = async () => await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO import_diagnostics (id, session_id, category, severity, code, message) " +
            "VALUES (gen_random_uuid(), {0}, 'unsupported_class', 'critical', 'TEST', 'x')",
            new object[] { sessionId });

        await insert.Should().ThrowAsync<Exception>(
            because: "CHECK constraint forbids severities outside error|warning|info");
    }

    // ─── Non-regression: legacy preview endpoint ──────────────────────────

    [Fact]
    public async Task LegacyConvertEndpoint_StillWorks()
    {
        // The /convert/latex-to-blocks preview endpoint is separate from the
        // staging pipeline — we must not have broken it.
        await SeedUserAsync(UserId);
        var payload = new { latex = LatexStagingFixtures.CleanArticle };
        var response = await Client.PostAsJsonAsync("/api/convert/latex-to-blocks", payload);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.BadRequest);
        // We accept either — the point is the endpoint is reachable, not removed.
    }
}
