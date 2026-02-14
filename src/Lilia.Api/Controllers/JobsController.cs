using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lilia.Api.Services;
using Lilia.Core.DTOs;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/lilia/jobs")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;
    private readonly ILogger<JobsController> _logger;
    private readonly IAuditService _auditService;

    public JobsController(IJobService jobService, ILogger<JobsController> logger, IAuditService auditService)
    {
        _jobService = jobService;
        _logger = logger;
        _auditService = auditService;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// List jobs for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<JobListDto>>> GetJobs(
        [FromQuery] string? status = null,
        [FromQuery] string? jobType = null,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var jobs = await _jobService.GetJobsAsync(userId, status, jobType, limit, offset);
        return Ok(jobs);
    }

    /// <summary>
    /// Get a specific job
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobDto>> GetJob(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var job = await _jobService.GetJobAsync(id, userId);
        if (job == null) return NotFound();

        return Ok(job);
    }

    /// <summary>
    /// Import a document from an external format
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> Import([FromBody] ImportRequestDto request)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        _logger.LogInformation(
            "[Import] User {UserId} importing file {Filename} format {Format}",
            userId, request.Filename, request.Format);

        try
        {
            var result = await _jobService.CreateImportJobFromBase64Async(userId, request);
            await _auditService.LogAsync("document.import", "Job", result.Job?.Id.ToString(), new { request.Filename, request.Format });
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Import] Failed to import file {Filename}", request.Filename);
            return StatusCode(500, new { message = "Import failed: " + ex.Message });
        }
    }

    /// <summary>
    /// Export a document to the specified format
    /// </summary>
    [HttpPost("export")]
    public async Task<ActionResult<ExportResultDto>> Export([FromBody] CreateExportJobDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        _logger.LogInformation(
            "[Export] User {UserId} exporting document {DocumentId} to {Format}",
            userId, dto.DocumentId, dto.Format);

        try
        {
            var job = await _jobService.CreateExportJobAsync(userId, dto);
            await _auditService.LogAsync("document.export", "Job", job.Id.ToString(), new { dto.DocumentId, dto.Format });

            // If completed, return the result directly
            if (job.Status == "COMPLETED")
            {
                var result = await _jobService.GetExportResultAsync(job.Id, userId);
                if (result != null)
                {
                    return Ok(result);
                }
            }

            // Return job info for tracking
            return Ok(new ExportResultDto(job.Id, job.Status, "", job.ResultFileName ?? ""));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Export] Failed to export document {DocumentId}", dto.DocumentId);
            return StatusCode(500, new { message = "Export failed: " + ex.Message });
        }
    }

    /// <summary>
    /// Get the result of an export job
    /// </summary>
    [HttpGet("{id:guid}/result")]
    public async Task<ActionResult<ExportResultDto>> GetExportResult(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _jobService.GetExportResultAsync(id, userId);
        if (result == null) return NotFound();

        return Ok(result);
    }

    /// <summary>
    /// Retry a failed job
    /// </summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<JobDto>> RetryJob(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var job = await _jobService.RetryJobAsync(id, userId);
            if (job == null) return NotFound();

            return Ok(job);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cancel a pending or processing job
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> CancelJob(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var result = await _jobService.CancelJobAsync(id, userId);
        if (!result) return NotFound();

        return NoContent();
    }
}
