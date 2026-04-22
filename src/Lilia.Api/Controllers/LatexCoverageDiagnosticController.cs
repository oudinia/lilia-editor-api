using System.Diagnostics;
using Lilia.Api.Services;
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
public class LatexCoverageDiagnosticController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly LiliaDbContext _db;

    public LatexCoverageDiagnosticController(
        ILatexCatalogService catalog,
        LiliaDbContext db)
    {
        _catalog = catalog;
        _db = db;
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
