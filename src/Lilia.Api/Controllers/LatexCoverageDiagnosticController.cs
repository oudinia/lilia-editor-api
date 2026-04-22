using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Lilia.Api.Services;
using Lilia.Import.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

// Diagnostic endpoint for the LaTeX catalog. Gated by a shared-secret
// header that must match Diagnostics:LatexCoverageToken. If the config
// value is unset the routes return 404 — disabled by default.
[ApiController]
[Route("api/public/latex-coverage/diag")]
[AllowAnonymous]
public class LatexCoverageDiagnosticController : ControllerBase
{
    private readonly ILatexCatalogService _catalog;
    private readonly LiliaDbContext _db;
    private readonly IConfiguration _config;

    public LatexCoverageDiagnosticController(
        ILatexCatalogService catalog,
        LiliaDbContext db,
        IConfiguration config)
    {
        _catalog = catalog;
        _db = db;
        _config = config;
    }

    [HttpGet("state")]
    public async Task<IActionResult> State(
        [FromHeader(Name = "X-Diag-Token")] string? token,
        CancellationToken ct)
    {
        if (GateOrNot(token) is IActionResult blocked) return blocked;

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
        [FromHeader(Name = "X-Diag-Token")] string? token,
        CancellationToken ct)
    {
        if (GateOrNot(token) is IActionResult blocked) return blocked;
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

    // Returns null if the request should proceed; otherwise the IActionResult
    // to short-circuit with (404 if disabled, 401 if wrong/missing token).
    private IActionResult? GateOrNot(string? provided)
    {
        var expected = _config["Diagnostics:LatexCoverageToken"];
        if (string.IsNullOrEmpty(expected)) return NotFound();
        if (string.IsNullOrEmpty(provided)) return Unauthorized();

        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(expected);
        if (a.Length != b.Length) return Unauthorized();
        return CryptographicOperations.FixedTimeEquals(a, b) ? null : Unauthorized();
    }
}

public sealed record DiagSelfTestRequest(string RawSource, Guid? SessionId = null);
