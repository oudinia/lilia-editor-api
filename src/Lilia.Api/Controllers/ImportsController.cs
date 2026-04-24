using System.Text;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Import.Services;
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
    private readonly ILatexProjectExtractor _projectExtractor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportsController> _logger;

    // Cap the direct upload at 15 MB — raised from 5 MB to fit typical
    // Overleaf projects with a photo / figures bundled in the .zip.
    // The per-file 5 MB cap inside LatexProjectExtractor still applies.
    private const long MaxUploadBytes = 15 * 1024 * 1024;

    public ImportsController(
        LiliaDbContext context,
        ILatexImportJobExecutor executor,
        ILatexProjectExtractor projectExtractor,
        IServiceScopeFactory scopeFactory,
        ILogger<ImportsController> logger)
    {
        _context = context;
        _executor = executor;
        _projectExtractor = projectExtractor;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Upload a .tex file or an Overleaf-style .zip project. For zips,
    /// <see cref="ILatexProjectExtractor"/> flattens \input/\include into
    /// a single resolved .tex, and the raw zip is preserved under
    /// uploads/imports/{jobId}.zip so the finalize step can stage image
    /// assets + .bib bibliography onto the finalized document.
    ///
    /// Creates an import review session with RawImportData (flattened
    /// .tex), enqueues a LATEX_IMPORT_V2 job, returns { sessionId, jobId }.
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

        // Read the whole file into memory once. Small enough (15 MB cap)
        // that we can hold it while deciding text-vs-zip.
        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms, ct);
            fileBytes = ms.ToArray();
        }

        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();

        // Zip detection: PK\x03\x04 magic bytes. Covers Overleaf exports
        // + any hand-zipped LaTeX project. Extension check as a guardrail
        // in case the client sent something misbranded.
        var isZip = fileBytes.Length >= 4 &&
                    fileBytes[0] == 0x50 && fileBytes[1] == 0x4B &&
                    fileBytes[2] == 0x03 && fileBytes[3] == 0x04;

        string source;
        string? preservedZipPath = null;
        var initialNotices = new List<string>();

        if (isZip)
        {
            try
            {
                var extracted = _projectExtractor.Extract(fileBytes);
                source = extracted.InlinedTex;
                initialNotices.AddRange(extracted.Notices);

                // Persist the zip so the finalize step can re-extract images
                // and the .bib without a second network round-trip.
                var importsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "uploads", "imports");
                Directory.CreateDirectory(importsDir);
                preservedZipPath = Path.Combine(importsDir, $"{jobId}.zip");
                await System.IO.File.WriteAllBytesAsync(preservedZipPath, fileBytes, ct);

                _logger.LogInformation(
                    "[ImportsController] Zip project {Filename} flattened → main {Main}, {ImgCount} images, {BibCount} .bib, {NoticeCount} notices",
                    file.FileName, extracted.MainFileName,
                    extracted.Files.Count(f => f.Kind == LatexProjectFileKinds.Image),
                    extracted.Files.Count(f => f.Kind == LatexProjectFileKinds.Bib),
                    extracted.Notices.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ImportsController] Failed to extract zip project {Filename}", file.FileName);
                return BadRequest(new { message = "Could not extract LaTeX project from zip: " + ex.Message });
            }
        }
        else
        {
            // Treat as plain .tex / .latex source. UTF-8 always.
            source = Encoding.UTF8.GetString(fileBytes);
        }

        if (string.IsNullOrWhiteSpace(source))
            return BadRequest(new { message = "file is empty" });

        var now = DateTime.UtcNow;
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
