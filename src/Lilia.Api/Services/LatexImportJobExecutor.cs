using System.Text.Json;
using Lilia.Core.Entities;
using Lilia.Import.Interfaces;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Services;

/// <summary>
/// Database-first LaTeX import pipeline. See plan doc
/// /home/oussama/.claude/plans/valiant-waddling-otter.md and
/// lilia-docs/docs/guidelines/import-export-db-first.md.
///
/// Flow: read raw source from DB → parse → bulk COPY block reviews →
/// bulk COPY diagnostics → SQL aggregate for QualityScore → decide
/// auto-finalize → optionally run INSERT...SELECT finalize. No loop-
/// and-SaveChanges on the hot path.
/// </summary>
public interface ILatexImportJobExecutor
{
    Task RunAsync(Guid jobId, Guid sessionId, CancellationToken ct = default);
}

public class LatexImportJobExecutor : ILatexImportJobExecutor
{
    private readonly LiliaDbContext _context;
    private readonly ILatexParser _latexParser;
    private readonly BulkInsertHelper _bulk;
    private readonly IImportReviewService _reviewService;
    private readonly IImportProgressService _progressService;
    private readonly ILogger<LatexImportJobExecutor> _logger;

    public LatexImportJobExecutor(
        LiliaDbContext context,
        ILatexParser latexParser,
        BulkInsertHelper bulk,
        IImportReviewService reviewService,
        IImportProgressService progressService,
        ILogger<LatexImportJobExecutor> logger)
    {
        _context = context;
        _latexParser = latexParser;
        _bulk = bulk;
        _reviewService = reviewService;
        _progressService = progressService;
        _logger = logger;
    }

    public async Task RunAsync(Guid jobId, Guid sessionId, CancellationToken ct = default)
    {
        var tracker = _progressService.CreateTracker(jobId.ToString());

        try
        {
            await tracker.ReportReceivingAsync("Loading source from staging realm...");

            // Phase 1 — Load. Single row, single column projected.
            var loaded = await _context.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.Id, s.RawImportData, s.AutoFinalizeEnabled, s.OwnerId, s.DocumentTitle })
                .FirstOrDefaultAsync(ct);

            if (loaded is null || string.IsNullOrEmpty(loaded.RawImportData))
            {
                throw new InvalidOperationException($"Session {sessionId} has no raw import data");
            }

            await MarkJobAsync(jobId, JobStatus.Processing, progress: 10, ct);

            // Phase 2 — Parse. Regex-walk the source once (unavoidable).
            await tracker.ReportParsingAsync("Parsing LaTeX source...");
            var parsed = await _latexParser.ParseTextAsync(loaded.RawImportData, new LatexImportOptions
            {
                ExtractDocumentTitle = true,
                ConvertEquationEnvironments = true,
                ConvertDisplayMath = true,
            });

            tracker.SetTotalBlocks(parsed.Elements.Count);
            await MarkJobAsync(jobId, JobStatus.Processing, progress: 40, ct);

            // Phase 3 — Stage blocks via COPY (zero EF tracking).
            await tracker.ReportConvertingBlocksAsync(0, parsed.Elements.Count);
            var stagedBlocks = await _bulk.BulkInsertBlockReviewsAsync(
                EnumerateBlockReviews(sessionId, parsed.Elements),
                ct);
            _logger.LogInformation("[LatexImport] Staged {Count} block reviews for session {Session}", stagedBlocks, sessionId);
            await MarkJobAsync(jobId, JobStatus.Processing, progress: 60, ct);

            // Phase 4 — Stage diagnostics via COPY.
            var stagedDiagnostics = await _bulk.BulkInsertDiagnosticsAsync(
                EnumerateDiagnostics(sessionId, parsed),
                ct);
            _logger.LogInformation("[LatexImport] Staged {Count} diagnostics for session {Session}", stagedDiagnostics, sessionId);
            await MarkJobAsync(jobId, JobStatus.Processing, progress: 80, ct);

            // Phase 5 — Score. Pure SQL aggregate.
            await _context.Database.ExecuteSqlRawAsync(@"
                UPDATE import_review_sessions SET quality_score = (
                  SELECT GREATEST(0, 100
                    - 30 * COUNT(*) FILTER (WHERE severity = 'error')
                    - 5  * COUNT(*) FILTER (WHERE severity = 'warning'
                                             AND category IN ('missing_asset','preamble_conflict'))
                    - 1  * COUNT(*) FILTER (WHERE severity = 'warning'
                                             AND category NOT IN ('missing_asset','preamble_conflict')))
                  FROM import_diagnostics WHERE session_id = {0})
                WHERE id = {0}", new object[] { sessionId }, ct);

            // Phase 6 — Decide status. Tiny projection.
            var decision = await _context.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .Select(s => new { s.QualityScore, s.AutoFinalizeEnabled })
                .FirstAsync(ct);

            var errorCount = await _context.ImportDiagnostics
                .CountAsync(d => d.SessionId == sessionId && d.Severity == "error", ct);
            var riskyWarningCount = await _context.ImportDiagnostics
                .CountAsync(d => d.SessionId == sessionId && d.Severity == "warning"
                                 && (d.Category == "missing_asset" || d.Category == "preamble_conflict"), ct);

            var isClean = errorCount == 0 && riskyWarningCount == 0 && stagedBlocks > 0;
            var shouldAutoFinalize = isClean && decision.AutoFinalizeEnabled;
            var newStatus = shouldAutoFinalize ? "auto_finalized" : "pending_review";

            await _context.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, newStatus)
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

            Guid? documentId = null;
            string? documentTitle = null;

            // Phase 7 — Auto-finalize if gate passes. INSERT...SELECT inside FinalizeAsync.
            if (shouldAutoFinalize)
            {
                var title = string.IsNullOrWhiteSpace(parsed.Title) ? loaded.DocumentTitle : parsed.Title;
                var finalized = await _reviewService.FinalizeFromStagingAsync(sessionId, loaded.OwnerId, title, force: true, ct);
                documentId = finalized.Document.Id;
                documentTitle = finalized.Document.Title;
            }

            // Phase 8 — Broadcast completion.
            await MarkJobAsync(jobId, JobStatus.Completed, progress: 100, ct);
            await tracker.ReportCompletedAsync(documentId?.ToString(), documentTitle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LatexImport] Job {JobId} failed for session {Session}", jobId, sessionId);
            await MarkJobAsync(jobId, JobStatus.Failed, progress: 0, ct, errorMessage: ex.Message);
            await _context.ImportReviewSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, "cancelled")
                    .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);
            await tracker.ReportFailedAsync(ex.Message);
            throw;
        }
    }

    // Yields ImportBlockReview rows lazily — never materialises the full list.
    private static IEnumerable<ImportBlockReview> EnumerateBlockReviews(Guid sessionId, List<ImportElement> elements)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var (type, content) = MapImportElementToBlock(elements[i]);
            yield return new ImportBlockReview
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                BlockIndex = i,
                BlockId = $"blk_{Guid.NewGuid():N}",
                Status = "pending",
                OriginalContent = JsonDocument.Parse(JsonSerializer.Serialize(content)),
                OriginalType = type,
                SortOrder = i * 100,
                Depth = 0,
            };
        }
    }

    private static IEnumerable<ImportDiagnostic> EnumerateDiagnostics(Guid sessionId, ImportDocument doc)
    {
        foreach (var w in doc.Warnings)
        {
            yield return DiagnosticMapper.Map(w, sessionId);
        }
    }

    // Mirror of JobService.MapImportElementToBlock — we don't reuse the private
    // JobService version so the two pipelines stay independent (new pipeline
    // must be free to diverge without breaking the legacy DOCX path).
    private static (string type, object content) MapImportElementToBlock(ImportElement element) => element switch
    {
        ImportHeading h => ("heading", new { text = h.Text, level = h.Level }),
        ImportParagraph p => ("paragraph", new { text = p.Text }),
        ImportEquation eq => ("equation", new { latex = eq.LatexContent ?? eq.OmmlXml, equationMode = eq.IsInline ? "inline" : "display" }),
        ImportCodeBlock c => ("code", new { code = c.Text, language = c.Language ?? "" }),
        ImportTable t => ("table", new
        {
            headers = t.HasHeaderRow && t.Rows.Count > 0
                ? t.Rows[0].Select(c => c.Text).ToArray()
                : Enumerable.Range(0, t.ColumnCount).Select(i => $"Column {i + 1}").ToArray(),
            rows = (t.HasHeaderRow ? t.Rows.Skip(1) : t.Rows).Select(r => r.Select(c => c.Text).ToArray()).ToArray()
        }),
        ImportAbstract a => ("abstract", new { text = a.Text }),
        ImportTheorem th => ("theorem", new { text = th.Text, theoremType = th.EnvironmentType.ToString().ToLowerInvariant(), title = th.Title ?? "", label = th.Label ?? "" }),
        ImportListItem li => ("list", new { items = new[] { li.Text }, ordered = li.IsNumbered }),
        ImportPageBreak => ("pageBreak", new { }),
        ImportImage img => ("figure", new { src = "", caption = img.AltText ?? "", alt = img.AltText ?? "" }),
        ImportLatexPassthrough lp => ("code", new { code = lp.LatexCode, language = "latex" }),
        _ => ("paragraph", new { text = "" }),
    };

    private async Task MarkJobAsync(Guid jobId, string status, int progress, CancellationToken ct, string? errorMessage = null)
    {
        await _context.Jobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(j => j
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Progress, progress)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow)
                .SetProperty(x => x.CompletedAt, status == JobStatus.Completed || status == JobStatus.Failed ? DateTime.UtcNow : (DateTime?)null)
                .SetProperty(x => x.ErrorMessage, errorMessage), ct);
    }
}
