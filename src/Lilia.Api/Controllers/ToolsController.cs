using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public, anonymous-friendly endpoints for the standalone tool suite. The
/// browser never calls this directly — lilia-cloud's BFF proxy forwards with the
/// anon identity (see the strategy doc §6.2). Gating is observe-friendly: free
/// quota + size cap, funnel events, no payment yet (one-off unlock is a follow-up).
/// </summary>
[ApiController]
[Route("api/tools")]
[AllowAnonymous]
public class ToolsController : ControllerBase
{
    private const string AnonCookie = "lilia_tool_anon";

    private readonly IToolCatalogService _catalog;
    private readonly IToolRunnerService _runner;
    private readonly LiliaDbContext _context;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        IToolCatalogService catalog,
        IToolRunnerService runner,
        LiliaDbContext context,
        ILogger<ToolsController> logger)
    {
        _catalog = catalog;
        _runner = runner;
        _context = context;
        _logger = logger;
    }

    /// <summary>The enabled tool registry (drives the lilia-cloud landers).</summary>
    [HttpGet]
    public IActionResult List() =>
        Ok(_catalog.Enabled().Select(ToDto));

    [HttpGet("{slug}")]
    public IActionResult Get(string slug)
    {
        var t = _catalog.Get(slug);
        return t is { Enabled: true } ? Ok(ToDto(t)) : NotFound();
    }

    /// <summary>Run a tool. JSON body for text/grid tools; multipart for file tools.</summary>
    [HttpPost("{slug}/run")]
    [RequestSizeLimit(25_000_000)]
    public async Task<IActionResult> Run(string slug, CancellationToken ct)
    {
        var tool = _catalog.Get(slug);
        if (tool is not { Enabled: true }) return NotFound();

        var anonId = GetOrSetAnonId();
        var userId = User.FindFirst("sub")?.Value;

        // ── read input (file or JSON) ───────────────────────────────────────
        IFormFile? file = null;
        var input = default(JsonElement);
        long inputSize;
        if (Request.HasFormContentType)
        {
            file = Request.Form.Files.FirstOrDefault();
            inputSize = file?.Length ?? 0;
        }
        else
        {
            try
            {
                input = await JsonSerializer.DeserializeAsync<JsonElement>(Request.Body, cancellationToken: ct);
            }
            catch
            {
                return BadRequest(new { message = "Invalid request body." });
            }
            inputSize = Request.ContentLength ?? 0;
        }

        // ── size cap (free tier) ────────────────────────────────────────────
        if (tool.FreeSizeCapBytes > 0 && inputSize > tool.FreeSizeCapBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                sizecap = true,
                message = $"That's larger than the free limit ({tool.FreeSizeCapBytes / 1024} KB). Sign in to lift the cap.",
            });
        }

        // ── quota (count today's successful uses for this anon + tool) ──────
        if (tool.FreeLimitPerDay > 0 && userId is null)
        {
            var since = DateTime.UtcNow.Date;
            var usedToday = await _context.ToolEvents.CountAsync(
                e => e.ToolSlug == slug && e.AnonId == anonId && e.Event == "use" && e.CreatedAt >= since, ct);
            if (usedToday >= tool.FreeLimitPerDay)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    quota = true,
                    message = $"You've used the free daily limit ({tool.FreeLimitPerDay}). Sign in to save your work and get more.",
                });
            }
        }

        // ── run (errors don't spend a use) ──────────────────────────────────
        ToolRunResult result;
        try
        {
            result = await _runner.RunAsync(tool, input, file, ct);
        }
        catch (ToolInputException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tools] run failed for {Slug}", slug);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "The tool failed to run. Your free use wasn't spent — try again." });
        }

        await RecordAsync(slug, userId, anonId, "use", ct);
        await RecordAsync(slug, userId, anonId, "result", ct);

        return Ok(new
        {
            output = result.Output,
            format = result.Format,
            title = result.Title,
            crossSell = new { label = tool.CrossSellLabel ?? "Open in Lilia editor", openInEditor = true },
        });
    }

    /// <summary>Funnel beacon — record view/signup/pay from the client.</summary>
    [HttpPost("{slug}/event")]
    public async Task<IActionResult> Event(string slug, [FromBody] ToolEventDto dto, CancellationToken ct)
    {
        if (_catalog.Get(slug) is null) return NotFound();
        var allowed = new[] { "view", "signup", "pay" };
        if (dto?.Event is null || !allowed.Contains(dto.Event)) return BadRequest();
        await RecordAsync(slug, User.FindFirst("sub")?.Value, GetOrSetAnonId(), dto.Event, ct);
        return NoContent();
    }

    public record ToolEventDto(string Event);

    // ── helpers ─────────────────────────────────────────────────────────────
    private static object ToDto(Tool t) => new
    {
        slug = t.Slug,
        title = t.Title,
        tagline = t.Tagline,
        seoDescription = t.SeoDescription,
        inputKind = t.InputKind,
        outputKind = t.OutputKind,
        freeLimitPerDay = t.FreeLimitPerDay,
        crossSellLabel = t.CrossSellLabel,
    };

    private string GetOrSetAnonId()
    {
        if (Request.Cookies.TryGetValue(AnonCookie, out var existing) && !string.IsNullOrWhiteSpace(existing))
            return existing;
        var id = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(AnonCookie, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
        });
        return id;
    }

    private async Task RecordAsync(string slug, string? userId, string anonId, string ev, CancellationToken ct)
    {
        try
        {
            _context.ToolEvents.Add(new ToolEvent
            {
                Id = Guid.NewGuid(),
                ToolSlug = slug,
                UserId = userId,
                AnonId = anonId,
                Event = ev,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Telemetry must never break the tool.
            _logger.LogWarning(ex, "[Tools] failed to record {Event} for {Slug}", ev, slug);
        }
    }
}
