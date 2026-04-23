using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Control-plane endpoints for the import domain (FT-IMP-001). A
/// definition is the immutable source + user — the thing you uploaded.
/// Instances are runs (= <see cref="ImportReviewSession"/> today, renamed
/// to rev_documents/rev_blocks in the PR 4 mirror-realm work). One
/// definition owns many instances: retry after failure creates a fresh
/// instance on the same definition without re-uploading.
///
/// Route: <c>/api/lilia/import-definitions</c>.
/// </summary>
[ApiController]
[Route("api/lilia/import-definitions")]
[Authorize]
public class ImportDefinitionsController : ControllerBase
{
    private readonly LiliaDbContext _context;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportDefinitionsController> _logger;

    public ImportDefinitionsController(
        LiliaDbContext context,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportDefinitionsController> logger)
    {
        _context = context;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Rerun the import. Creates a new instance on the given definition,
    /// marks the previous latest-non-terminal instance as
    /// <c>superseded</c>, and kicks a fresh parse job. The source file
    /// persists on the definition — rerun doesn't require re-upload.
    ///
    /// Returns the new instance's <c>sessionId</c> so the client can
    /// navigate to the review page.
    /// </summary>
    [HttpPost("{id:guid}/rerun")]
    public async Task<ActionResult<RerunResponse>> Rerun(Guid id, CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var definition = await _context.ImportDefinitions
            .FirstOrDefaultAsync(d => d.Id == id, ct);
        if (definition == null) return NotFound(new { message = "Definition not found." });
        if (definition.OwnerId != userId) return Forbid();

        // Mark any still-running / pending-review instance as superseded
        // so the user's history list shows just the active one. Terminal
        // instances (imported / cancelled) are left alone — they're
        // audit history, not candidates for supersession.
        var supersedeStates = new[] { "parsing", "pending_review", "auto_finalized", "in_progress", "failed" };
        var affected = await _context.ImportReviewSessions
            .Where(s => s.DefinitionId == id && supersedeStates.Contains(s.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, "superseded")
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), ct);

        // Spawn a new instance on the same definition with a fresh job.
        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        _context.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = userId,
            UserId = userId,
            JobType = JobTypes.Import,
            Status = JobStatus.Pending,
            Progress = 0,
            SourceFormat = definition.SourceFormat,
            TargetFormat = "lilia",
            SourceFileName = definition.SourceFileName,
            Direction = "INBOUND",
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            DefinitionId = id,
            JobId = jobId,
            OwnerId = userId,
            DocumentTitle = System.IO.Path.GetFileNameWithoutExtension(definition.SourceFileName),
            Status = "parsing",
            RawImportData = definition.RawSource,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(30),
            SourceFormat = definition.SourceFormat,
        });
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[ImportRerun] definition={DefId} supersededCount={N} newSession={SessionId}",
            id, affected, sessionId);

        // Fire-and-forget the parse job for the new instance. Same pattern
        // as the original upload path.
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var jobExec = scope.ServiceProvider.GetRequiredService<ILatexImportJobExecutor>();
            try
            {
                await jobExec.RunAsync(jobId, sessionId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ImportRerun] Background parse failed for rerun job {JobId}", jobId);
            }
        });

        return Ok(new RerunResponse(sessionId, jobId, affected));
    }
}

public sealed record RerunResponse(Guid SessionId, Guid JobId, int SupersededCount);
