using System.Text.Json;
using Lilia.Engines;
using Lilia.Tools.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Tools.Api.Controllers;

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
    private readonly IEntitlementService _entitlements;
    private readonly LiliaDbContext _context;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        IToolCatalogService catalog,
        IToolRunnerService runner,
        IEntitlementService entitlements,
        LiliaDbContext context,
        ILogger<ToolsController> logger)
    {
        _catalog = catalog;
        _runner = runner;
        _entitlements = entitlements;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// What this caller may do. The client renders its paywall from this; the server
    /// still checks every paid action independently, so a modified client gains nothing.
    /// </summary>
    [HttpGet("entitlement")]
    public IActionResult GetEntitlement()
    {
        var e = _entitlements.Resolve(User.FindFirst("sub")?.Value);
        return Ok(new
        {
            tier = e.Tier.ToString(),
            canUseExportPresets = e.CanUseExportPresets,
            canSaveStyles = e.CanSaveStyles,
        });
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

        // ── paid capabilities ───────────────────────────────────────────────
        // Export presets are the paid unlock; the LaTeX source is always free and
        // never watermarked. Checked here rather than trusted from the client, so
        // the paywall holds against a hand-rolled request. No payment provider is
        // wired yet — see EntitlementService — but the gate is real from day one.
        var exportPreset = input.ValueKind == JsonValueKind.Object
            && input.TryGetProperty("exportPreset", out var presetProp)
            && presetProp.ValueKind == JsonValueKind.String
                ? presetProp.GetString()
                : null;

        if (!string.IsNullOrWhiteSpace(exportPreset))
        {
            var entitlement = _entitlements.Resolve(userId);
            if (!entitlement.CanUseExportPresets)
            {
                return StatusCode(StatusCodes.Status402PaymentRequired, new
                {
                    upgrade = true,
                    tier = entitlement.Tier.ToString(),
                    requires = nameof(ToolTier.DocumentPass),
                    message = "Export presets need a document pass. Copying the LaTeX source is always free.",
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
        var artifactId = await RecordArtifactAsync(slug, userId, anonId, input, file, result, ct);

        return Ok(new
        {
            output = result.Output,
            format = result.Format,
            title = result.Title,
            artifactId,
            // Absent for engines that don't compile anything; the client must treat a
            // missing verdict as "unchecked" rather than as a pass.
            verdict = result.Verdict is null ? null : new
            {
                status = result.Verdict.Status,
                findings = result.Verdict.Findings,
                durationMs = result.Verdict.DurationMs,
                engine = result.Verdict.Engine,
                engineAuto = result.Verdict.EngineAuto,
            },
            crossSell = new { label = tool.CrossSellLabel ?? "Open in Lilia editor", openInEditor = true },
        });
    }

    // "Open in Lilia" used to live here, creating a document through the editor's
    // IDocumentService/IBlockService. That pointed the dependency the wrong way:
    // the public tools host reaching into the editor's internals. It now lives in
    // Lilia.Api (FromToolController) and reads the artifact this host wrote. The
    // editor pulls; tools never call the editor. See the artifact model note in
    // lilia-docs/features/2026-06-22-standalone-tools-strategy.md §9.

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

    private const int MaxArtifactBytes = 262_144; // 256 KB — cap stored output

    // Persist the run (input + output) for behaviour/pattern analytics + the
    // future library. Ephemeral for the user; prunable for us. Best-effort.
    private async Task<Guid?> RecordArtifactAsync(
        string slug, string? userId, string anonId,
        JsonElement input, IFormFile? file, ToolRunResult result, CancellationToken ct)
    {
        try
        {
            JsonDocument? inputDoc = file is not null
                ? JsonDocument.Parse(JsonSerializer.Serialize(new { filename = file.FileName, bytes = file.Length }))
                : input.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    ? JsonDocument.Parse(input.GetRawText())
                    : null;

            var output = result.Output ?? string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetByteCount(output);
            var truncated = output.Length > MaxArtifactBytes;
            if (truncated) output = output[..MaxArtifactBytes];

            var id = Guid.NewGuid();
            _context.ToolArtifacts.Add(new ToolArtifact
            {
                Id = id,
                ToolSlug = slug,
                UserId = userId,
                AnonId = anonId,
                Input = inputDoc,
                Output = output,
                OutputFormat = result.Format,
                OutputBytes = bytes,
                Truncated = truncated,
                CreatedAt = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync(ct);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Tools] failed to record artifact for {Slug}", slug);
            return null;
        }
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
