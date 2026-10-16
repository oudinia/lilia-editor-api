using System.Text.Json;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

[ApiController]
[Route("api/feedback")]
[Authorize]
public class FeedbackController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly ILogger<FeedbackController> _logger;
    private readonly IConfiguration _config;

    public FeedbackController(LiliaDbContext db, ILogger<FeedbackController> logger, IConfiguration config)
    {
        _db = db;
        _logger = logger;
        _config = config;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Submit feedback. Allows anonymous submissions.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<FeedbackDto>> CreateFeedback([FromBody] CreateFeedbackDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest("Message is required.");

        var validTypes = new[] { "bug", "feature", "ux", "question", "general" };
        var type = validTypes.Contains(dto.Type) ? dto.Type : "general";

        var feedback = new Feedback
        {
            Id = Guid.NewGuid(),
            UserId = GetUserId(),
            Type = type,
            Message = dto.Message.Trim(),
            Page = dto.Page,
            BlockType = dto.BlockType,
            BlockId = dto.BlockId,
            DocumentId = dto.DocumentId,
            Status = "new",
            Metadata = dto.Metadata.HasValue
                ? JsonDocument.Parse(dto.Metadata.Value.GetRawText())
                : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Feedback.Add(feedback);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Feedback {Id} submitted (type={Type}, user={UserId})", feedback.Id, feedback.Type, feedback.UserId ?? "anonymous");

        var result = MapToDto(feedback, userName: null);
        return CreatedAtAction(nameof(GetFeedback), new { id = feedback.Id }, result);
    }

    /// <summary>
    /// List feedback with optional filters.
    /// </summary>
    // Triage is for operators (Auth:Admins): it asked only for a login, so any signed-in account
    // could read, change and count everyone's feedback (16 Oct).
    [HttpGet]
    [Authorize(Policy = Lilia.Api.Security.AdminPolicy.Name)]
    public async Task<ActionResult<List<FeedbackDto>>> ListFeedback(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var query = _db.Feedback
            .Include(f => f.User)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(f => f.Status == status);

        if (!string.IsNullOrEmpty(type))
            query = query.Where(f => f.Type == type);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(Math.Min(limit, 200))
            .ToListAsync();

        var dtos = items.Select(f => MapToDto(f, f.User?.Name)).ToList();
        return Ok(dtos);
    }

    /// <summary>
    /// Get a single feedback entry by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FeedbackDto>> GetFeedback(Guid id)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var feedback = await _db.Feedback
            .Include(f => f.User)
            .FirstOrDefaultAsync(f => f.Id == id);

        // The author's own, or an admin's: anyone else gets the same 404 as a missing one.
        if (feedback == null
            || (feedback.UserId != userId && !Lilia.Api.Security.AdminPolicy.IsAdmin(User, _config["Auth:Admins"])))
            return NotFound();

        return Ok(MapToDto(feedback, feedback.User?.Name));
    }

    /// <summary>
    /// Update feedback status or add an admin response.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [Authorize(Policy = Lilia.Api.Security.AdminPolicy.Name)]
    public async Task<ActionResult<FeedbackDto>> UpdateFeedback(Guid id, [FromBody] UpdateFeedbackDto dto)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var feedback = await _db.Feedback
            .Include(f => f.User)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (feedback == null) return NotFound();

        var validStatuses = new[] { "new", "acknowledged", "in-progress", "resolved", "dismissed" };

        if (!string.IsNullOrEmpty(dto.Status))
        {
            if (!validStatuses.Contains(dto.Status))
                return BadRequest($"Invalid status. Valid values: {string.Join(", ", validStatuses)}");
            feedback.Status = dto.Status;
        }

        if (dto.Response != null)
            feedback.Response = dto.Response;

        feedback.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(MapToDto(feedback, feedback.User?.Name));
    }

    /// <summary>
    /// Get summary counts by type and status.
    /// </summary>
    [HttpGet("stats")]
    [Authorize(Policy = Lilia.Api.Security.AdminPolicy.Name)]
    public async Task<ActionResult<object>> GetStats()
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var byStatus = await _db.Feedback
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var byType = await _db.Feedback
            .GroupBy(f => f.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = await _db.Feedback.CountAsync();

        return Ok(new
        {
            total,
            byStatus = byStatus.ToDictionary(x => x.Status, x => x.Count),
            byType = byType.ToDictionary(x => x.Type, x => x.Count)
        });
    }

    private static FeedbackDto MapToDto(Feedback f, string? userName)
    {
        JsonElement? metadata = null;
        if (f.Metadata != null)
            metadata = f.Metadata.RootElement.Clone();

        return new FeedbackDto(
            Id: f.Id,
            UserId: f.UserId,
            UserName: userName,
            Type: f.Type,
            Message: f.Message,
            Page: f.Page,
            BlockType: f.BlockType,
            DocumentId: f.DocumentId,
            Status: f.Status,
            Response: f.Response,
            Metadata: metadata,
            CreatedAt: f.CreatedAt
        );
    }
}
