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
