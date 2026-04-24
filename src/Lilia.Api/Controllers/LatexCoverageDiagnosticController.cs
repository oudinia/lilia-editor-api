using System.Diagnostics;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

// Temporary diagnostic surface for the LaTeX catalog pipeline. Open
// (AllowAnonymous, no header gate) while we hunt why latex_token_usage
// stays empty in prod despite successful imports. Remove once fixed.
[ApiController]
[Route("api/public/latex-coverage/diag")]
[AllowAnonymous]
public partial class LatexCoverageDiagnosticController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly LiliaDbContext _db;
    private readonly ILatexImportJobExecutor _executor;

    public LatexCoverageDiagnosticController(
        ILatexCatalogService catalog,
        LiliaDbContext db,
        ILatexImportJobExecutor executor)
    {
        _catalog = catalog;
        _db = db;
        _executor = executor;
    }

    /// <summary>
    /// Returns every piece of data the editor UI surfaces will consume
    /// for a given LaTeX body — the pre-upload modal, the paste-flow
    /// preview, and (via the same shape) the import-review Coverage
    /// tab. Lets the front-end be built against a concrete contract
    /// and lets us test the data pipeline in isolation via curl +
    /// fixtures, no UI required.
    ///
    /// Response shape matches the five UI surfaces documented in
    /// lilia-docs/technical/latex-coverage-editor-ui-integration.md.
    /// </summary>
    [HttpPost("ui-preview")]
    public async Task<IActionResult> UiPreview(
        [FromBody] DiagUiPreviewRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RawSource))
            return BadRequest(new { error = "rawSource required" });

        // Scan the source — same path the real import takes at Phase 4.5.
        var scan = LatexCatalogTokenScanner.Scan(req.RawSource);

        // Per-token resolution against the catalog. For unknowns we do
        // NOT auto-insert — this endpoint is side-effect-free (the
        // live /diag/self-test and /diag/run-import do insertion).
        var rows = new List<TokenResolutionRow>(scan.Count);
        foreach (var (key, count) in scan)
        {
            var entry = _catalog.LookupToken(key.Name, key.Kind);
            rows.Add(new TokenResolutionRow(
                Name: key.Name,
                Kind: key.Kind,
                Count: count,
                CoverageLevel: entry?.CoverageLevel ?? "unsupported",
                HandlerKind: entry?.HandlerKind,
                MapsToBlockType: entry?.MapsToBlockType,
                // CatalogTokenEntry doesn't carry Notes in the cached
                // shape — those live only in the DB. We skip surfacing
                // them here; the UI can link to /tokens/{name} if
                // users want the note text.
                Notes: null,
                PackageSlug: entry?.PackageSlug));
        }

        // Aggregate by coverage level (counts of distinct tokens, not
        // usage occurrences — matches how the public Hero counts).
        var byLevel = new Dictionary<string, UiLevelSummary>
        {
            ["full"]        = new(0, new List<string>()),
            ["partial"]     = new(0, new List<string>()),
            ["shimmed"]     = new(0, new List<string>()),
            ["none"]        = new(0, new List<string>()),
            ["unsupported"] = new(0, new List<string>()),
        };
        foreach (var r in rows)
        {
            var bucket = byLevel[r.CoverageLevel];
            var display = r.Kind == "environment" ? $"\\begin{{{r.Name}}}" : $"\\{r.Name}";
            // Keep a small sample per bucket (up to 10) so the UI
            // can show users "here's what these look like." Full
            // list lives in `tokens` for drill-down.
            if (bucket.Examples.Count < 10)
                bucket.Examples.Add(display);
            byLevel[r.CoverageLevel] = bucket with { Count = bucket.Count + 1 };
        }

        // Aggregate by handler_kind — feeds the facet chips.
        var byHandler = rows
            .Where(r => r.HandlerKind is not null)
            .GroupBy(r => r.HandlerKind!)
            .Select(g => new UiHandlerSummary(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // Headline numbers — the two Hero stats.
        var distinctTokens = rows.Count;
        var renderedNatively = byLevel["full"].Count + byLevel["partial"].Count + byLevel["shimmed"].Count;
        var preservedAsSource = byLevel["none"].Count + byLevel["unsupported"].Count;
        var renderedPct = distinctTokens == 0 ? 100.0 : Math.Round(100.0 * renderedNatively / distinctTokens, 1);
        var preservedPct = distinctTokens == 0 ? 0.0 : Math.Round(100.0 * preservedAsSource / distinctTokens, 1);

        // Per-token detail only for the unsupported bucket — the
        // actionable one for the pre-upload modal. Other buckets live
        // in `tokens` for full drill-down.
        var unsupportedDetail = rows
            .Where(r => r.CoverageLevel == "unsupported")
            .OrderByDescending(r => r.Count)
            .Select(r => new UiUnsupportedToken(
                Name: r.Name,
                Kind: r.Kind,
                Occurrences: r.Count,
                Guidance: r.Kind == "environment"
                    ? $"Unknown environment — body will be preserved as raw LaTeX in an embed block; it round-trips on export."
                    : $"Unknown command — literal '\\{r.Name}' may appear in paragraph text if the command has no arguments; if it takes an argument, the argument survives."))
            .ToList();

        // Copy helpers — opinionated defaults the UI can display verbatim
        // or override. Kept here so every surface uses the same phrasing
        // (consistency + localisation later).
        var headline = distinctTokens == 0
            ? "Nothing to import yet."
            : $"Lilia will render {renderedPct:0.#}% of this document natively.";
        var subheading = preservedAsSource == 0
            ? "Every token in this document is handled — no raw LaTeX will leak through."
            : $"The remaining {preservedPct:0.#}% — {preservedAsSource} token{(preservedAsSource == 1 ? "" : "s")} — will be preserved as raw LaTeX. Nothing is lost; everything round-trips on export.";

        return Ok(new DiagUiPreviewResponse(
            Summary: new UiSummary(
                TotalTokens: distinctTokens,
                TotalOccurrences: rows.Sum(r => r.Count),
                RenderedNatively: renderedNatively,
                PreservedAsSource: preservedAsSource,
                RenderedPercent: renderedPct,
                PreservedPercent: preservedPct),
            ByLevel: byLevel,
            ByHandler: byHandler,
            UnsupportedTokens: unsupportedDetail,
            Tokens: rows,
            Copy: new UiCopy(
                Headline: headline,
                Subheading: subheading,
                CtaProceed: preservedAsSource == 0 ? "Import" : "Import anyway",
                CtaCancel: "Cancel")));
    }

    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken ct)
    {

        var tokens = await _db.LatexTokens.CountAsync(ct);
        var packages = await _db.LatexPackages.CountAsync(ct);
        var classes = await _db.LatexDocumentClasses.CountAsync(ct);
        var usageRows = await _db.LatexTokenUsages.CountAsync(ct);

        var coverageDistribution = await _db.LatexTokens
            .GroupBy(t => t.CoverageLevel)
            .Select(g => new { level = g.Key, n = g.Count() })
            .ToListAsync(ct);

        var recentTexSessions = await _db.ImportReviewSessions
            .Where(s => s.SourceFormat == "tex")
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new
            {
                id = s.Id,
                sourceFormat = s.SourceFormat,
                status = s.Status,
                createdAt = s.CreatedAt,
                rawLen = s.RawImportData == null ? 0 : s.RawImportData.Length,
                usageCount = _db.LatexTokenUsages.Count(u => u.SessionId == s.Id),
                blockCount = _db.ImportBlockReviews.Count(b => b.SessionId == s.Id)
            })
            .ToListAsync(ct);

        return Ok(new
        {
            catalog = new { tokens, packages, classes, usageRows },
            tokenCoverage = coverageDistribution,
            recentTexSessions
        });
    }

    [HttpPost("self-test")]
    public async Task<IActionResult> SelfTest(
        [FromBody] DiagSelfTestRequest req,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.RawSource))
            return BadRequest(new { error = "rawSource required" });

        var sw = Stopwatch.StartNew();

        var scan = LatexCatalogTokenScanner.Scan(req.RawSource);
        var lookup = new List<object>();
        var usages = new List<CatalogTokenUsage>();
        foreach (var (key, count) in scan)
        {
            var entry = _catalog.LookupToken(key.Name, key.Kind);
            if (entry is not null)
            {
                lookup.Add(new
                {
                    name = key.Name,
                    kind = key.Kind,
                    count,
                    status = "found",
                    tokenId = entry.Id,
                    coverageLevel = entry.CoverageLevel,
                    mapsToBlockType = entry.MapsToBlockType
                });
                usages.Add(new CatalogTokenUsage(entry.Id, count));
            }
            else
            {
                lookup.Add(new { name = key.Name, kind = key.Kind, count, status = "unknown" });
            }
        }

        object? writeResult = null;
        if (req.SessionId is Guid sessId && usages.Count > 0)
        {
            var exists = await _db.ImportReviewSessions.AnyAsync(s => s.Id == sessId, ct);
            if (!exists)
            {
                writeResult = new { status = "error", reason = "session_not_found", sessionId = sessId };
            }
            else
            {
                var before = await _db.LatexTokenUsages.CountAsync(u => u.SessionId == sessId, ct);
                object? exception = null;
                try
                {
                    await _catalog.RecordUsageAsync(sessId, usages, ct);
                }
                catch (Exception ex)
                {
                    exception = new
                    {
                        type = ex.GetType().FullName,
                        message = ex.Message,
                        innerType = ex.InnerException?.GetType().FullName,
                        innerMessage = ex.InnerException?.Message,
                        stack = ex.ToString()
                    };
                }
                var after = await _db.LatexTokenUsages.CountAsync(u => u.SessionId == sessId, ct);
                writeResult = new
                {
                    sessionId = sessId,
                    usageRecordsAttempted = usages.Count,
                    rowsBefore = before,
                    rowsAfter = after,
                    rowsDelta = after - before,
                    exception
                };
            }
        }

        sw.Stop();
        return Ok(new
        {
            elapsedMs = sw.ElapsedMilliseconds,
            scannedDistinct = scan.Count,
            scannedTotal = scan.Values.Sum(),
            lookup,
            writeAttempted = req.SessionId.HasValue,
            writeResult
        });
    }

}

public sealed record DiagSelfTestRequest(string RawSource, Guid? SessionId = null);

// UI-preview contract — every surface in
// lilia-docs/technical/latex-coverage-editor-ui-integration.md
// consumes one of these shapes.
public sealed record DiagUiPreviewRequest(string RawSource);

public sealed record TokenResolutionRow(
    string Name,
    string Kind,
    int Count,
    string CoverageLevel,
    string? HandlerKind,
    string? MapsToBlockType,
    string? Notes,
    string? PackageSlug);

public sealed record UiLevelSummary(int Count, List<string> Examples);

public sealed record UiHandlerSummary(string Kind, int Count);

public sealed record UiUnsupportedToken(string Name, string Kind, int Occurrences, string Guidance);

public sealed record UiSummary(
    int TotalTokens,
    int TotalOccurrences,
    int RenderedNatively,
    int PreservedAsSource,
    double RenderedPercent,
    double PreservedPercent);

public sealed record UiCopy(string Headline, string Subheading, string CtaProceed, string CtaCancel);

public sealed record DiagUiPreviewResponse(
    UiSummary Summary,
    Dictionary<string, UiLevelSummary> ByLevel,
    List<UiHandlerSummary> ByHandler,
    List<UiUnsupportedToken> UnsupportedTokens,
    List<TokenResolutionRow> Tokens,
    UiCopy Copy);

public sealed record DiagRunImportRequest(string RawSource, string? OwnerId = null, string? Title = null);

// Injected as an extension on the existing controller via a partial class
// would be cleaner, but the controller is small — keeping it one file.
public partial class LatexCoverageDiagnosticController
{
    // Drives the full production import pipeline end-to-end against a caller-
    // supplied LaTeX source. Bypasses upload + auth by creating the session +
    // job directly and awaiting the executor inline. Returns the final DB
    // state (block / diagnostic / usage counts) so the caller can verify
    // exactly what the pipeline produced. Intended for catalog-coverage
    // diagnosis; do not wire into any user-facing flow.
    //
    // ?keep=true preserves the session for follow-up inspection. Default is
    // to delete the session + job (cascades usage / block / diagnostic rows)
    // after counts are captured, so runs don't pollute owner session lists.
    [HttpPost("run-import")]
    public async Task<IActionResult> RunImport(
        [FromBody] DiagRunImportRequest req,
        [FromQuery] bool keep = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(req.RawSource))
            return BadRequest(new { error = "rawSource required" });

        var ownerId = req.OwnerId;
        if (string.IsNullOrEmpty(ownerId))
        {
            ownerId = await _db.Users.OrderBy(u => u.CreatedAt).Select(u => u.Id).FirstOrDefaultAsync(ct);
            if (string.IsNullOrEmpty(ownerId))
                return BadRequest(new { error = "no user in DB; pass ownerId" });
        }

        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var title = string.IsNullOrEmpty(req.Title)
            ? $"[diag] {now:yyyy-MM-dd HH:mm:ss}"
            : req.Title;

        _db.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = ownerId,
            UserId = ownerId,
            JobType = JobTypes.Import,
            Status = JobStatus.Pending,
            Progress = 0,
            SourceFormat = "latex",
            TargetFormat = "lilia",
            SourceFileName = "diag-fixture.tex",
            InputFileSize = req.RawSource.Length,
            Direction = "INBOUND",
            CreatedAt = now,
            UpdatedAt = now,
        });
        _db.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            JobId = jobId,
            OwnerId = ownerId,
            DocumentTitle = title,
            SourceFormat = "tex",
            Status = "parsing",
            RawImportData = req.RawSource,
            AutoFinalizeEnabled = false,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(1),
        });
        await _db.SaveChangesAsync(ct);

        var sw = Stopwatch.StartNew();
        object? runException = null;
        try
        {
            await _executor.RunAsync(jobId, sessionId, ct);
        }
        catch (Exception ex)
        {
            runException = new
            {
                type = ex.GetType().FullName,
                message = ex.Message,
                innerType = ex.InnerException?.GetType().FullName,
                innerMessage = ex.InnerException?.Message,
                stack = ex.ToString()
            };
        }
        sw.Stop();

        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct);
        var blockCount = await _db.ImportBlockReviews.CountAsync(b => b.SessionId == sessionId, ct);
        var diagCount = await _db.ImportDiagnostics.CountAsync(d => d.SessionId == sessionId, ct);
        var usageCount = await _db.LatexTokenUsages.CountAsync(u => u.SessionId == sessionId, ct);

        var cleaned = false;
        if (!keep)
        {
            // Cascade from import_review_sessions deletes block reviews,
            // diagnostics, usage rows, activities, collaborators, comments.
            // Job row has no cascade — delete it separately.
            await _db.ImportReviewSessions.Where(s => s.Id == sessionId).ExecuteDeleteAsync(ct);
            await _db.Jobs.Where(j => j.Id == jobId).ExecuteDeleteAsync(ct);
            cleaned = true;
        }

        return Ok(new
        {
            sessionId,
            jobId,
            ownerId,
            elapsedMs = sw.ElapsedMilliseconds,
            jobStatus = job?.Status,
            jobProgress = job?.Progress,
            jobError = job?.ErrorMessage,
            blockCount,
            diagnosticCount = diagCount,
            usageCount,
            runException,
            cleaned
        });
    }

    // Sweep up any diag sessions left behind (e.g. ?keep=true runs whose caller
    // forgot to clean up, or historical pollution from earlier diag passes).
    // Matches on document_title LIKE '[diag]%' which is the title prefix set
    // by RunImport. Returns counts of each row type removed via cascade.
    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupDiagSessions(CancellationToken ct)
    {
        var ids = await _db.ImportReviewSessions
            .Where(s => EF.Functions.Like(s.DocumentTitle, "[diag]%"))
            .Select(s => new { s.Id, s.JobId })
            .ToListAsync(ct);

        if (ids.Count == 0)
            return Ok(new { sessionsRemoved = 0, jobsRemoved = 0 });

        var sessionIds = ids.Select(x => x.Id).ToArray();
        var jobIds = ids.Where(x => x.JobId.HasValue).Select(x => x.JobId!.Value).ToArray();

        var sessionsRemoved = await _db.ImportReviewSessions
            .Where(s => sessionIds.Contains(s.Id))
            .ExecuteDeleteAsync(ct);
        var jobsRemoved = await _db.Jobs
            .Where(j => jobIds.Contains(j.Id))
            .ExecuteDeleteAsync(ct);

        return Ok(new { sessionsRemoved, jobsRemoved });
    }
}
