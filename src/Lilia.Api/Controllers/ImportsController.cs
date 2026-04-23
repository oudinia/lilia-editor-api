using System.Text;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Database-first import entry points. Distinct from the legacy
/// <c>/convert/latex-to-blocks</c> endpoint (stateless preview path) —
/// these endpoints stage every import in the review realm, so the parser
/// job can work from DB rather than from an HTTP body.
/// </summary>
[ApiController]
[Route("api/lilia/imports")]
[Authorize]
public class ImportsController : ControllerBase
{
    private readonly LiliaDbContext _context;
    private readonly ILatexImportJobExecutor _executor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportsController> _logger;

    // Cap the direct upload at 5 MB for now. Parser is still regex-walk on a
    // single string, so the memory ceiling is the parser's working set. Raise
    // when the parser becomes stream-friendly or when the multi-file project
    // ingest layer lands (see plan §"Future layer").
    private const long MaxUploadBytes = 5 * 1024 * 1024;

    public ImportsController(
        LiliaDbContext context,
        ILatexImportJobExecutor executor,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportsController> logger)
    {
        _context = context;
        _executor = executor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Upload a .tex file. Creates an import review session with RawImportData,
    /// enqueues a LATEX_IMPORT_V2 job, returns { sessionId, jobId }. Client
    /// subscribes to ImportHub group "import-{jobId}" for progress.
    /// </summary>
    [HttpPost("latex")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<ActionResult<LatexImportUploadResponseDto>> UploadLatex(
        IFormFile file,
        [FromQuery] bool autoFinalize = false,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (file is null || file.Length == 0) return BadRequest(new { message = "file is required" });
        if (file.Length > MaxUploadBytes) return BadRequest(new { message = $"file exceeds {MaxUploadBytes / 1024 / 1024} MB cap" });

        // Read into a string once. Parser will regex-walk from here; we don't
        // keep a second copy after persisting to RawImportData.
        string source;
        using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
        {
            source = await reader.ReadToEndAsync(ct);
        }
        if (string.IsNullOrWhiteSpace(source))
            return BadRequest(new { message = "file is empty" });

        var now = DateTime.UtcNow;
        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var title = Path.GetFileNameWithoutExtension(file.FileName ?? "Imported LaTeX");

        // FT-IMP-001: every upload creates an ImportDefinition (the
        // immutable source) + an initial ImportReviewSession (the first
        // instance). Retry later creates a new instance on the same
        // definition without re-uploading — see
        // POST /api/lilia/import-definitions/{id}/rerun.
        _context.ImportDefinitions.Add(new ImportDefinition
        {
            Id = definitionId,
            OwnerId = userId,
            SourceFileName = file.FileName ?? $"{title}.tex",
            SourceFormat = "latex",
            RawSource = source,
            CreatedAt = now,
        });

        // Persist the job + session in one SaveChanges. Both rows fit — no
        // bulk helper needed.
        _context.Jobs.Add(new Job
        {
            Id = jobId,
            TenantId = userId,
            UserId = userId,
            JobType = JobTypes.Import,
            Status = JobStatus.Pending,
            Progress = 0,
            SourceFormat = "latex",
            TargetFormat = "lilia",
            SourceFileName = file.FileName,
            InputFileSize = file.Length,
            Direction = "INBOUND",
            CreatedAt = now,
            UpdatedAt = now,
        });
        _context.ImportReviewSessions.Add(new ImportReviewSession
        {
            Id = sessionId,
            DefinitionId = definitionId,
            JobId = jobId,
            OwnerId = userId,
            DocumentTitle = title,
            Status = "parsing",
            RawImportData = source,
            AutoFinalizeEnabled = autoFinalize,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddDays(30),
        });
        await _context.SaveChangesAsync(ct);

        // Fire-and-forget the parse job. Uses its own DI scope so the captured
        // DbContext lives only as long as the job. Same pattern as the Task.Run
        // fix in BlocksController (Npgsql concurrency, see project memory).
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
                _logger.LogError(ex, "[ImportsController] Background LaTeX import failed for job {JobId}", jobId);
            }
        });

        return Ok(new LatexImportUploadResponseDto(sessionId, jobId));
    }
}
