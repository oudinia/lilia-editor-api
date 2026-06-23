using System.Text.Json;
using Lilia.Api.Models.Documents;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
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
    private readonly IDocumentService _documents;
    private readonly IBlockService _blocks;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        IToolCatalogService catalog,
        IToolRunnerService runner,
        LiliaDbContext context,
        IDocumentService documents,
        IBlockService blocks,
        ILogger<ToolsController> logger)
    {
        _catalog = catalog;
        _runner = runner;
        _context = context;
        _documents = documents;
        _blocks = blocks;
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
        var artifactId = await RecordArtifactAsync(slug, userId, anonId, input, file, result, ct);

        return Ok(new
        {
            output = result.Output,
            format = result.Format,
            title = result.Title,
            artifactId,
            crossSell = new { label = tool.CrossSellLabel ?? "Open in Lilia editor", openInEditor = true },
        });
    }

    /// <summary>
    /// The "Open in Lilia" cross-sell destination — create a real document (owned by
    /// the signed-in user) from a tool artifact, and return its id. The editor route
    /// calls this then redirects into the doc. Word→LaTeX is excluded here (it routes
    /// into import-review, not a flat doc).
    /// </summary>
    [HttpPost("artifacts/{id:guid}/to-document")]
    [Authorize]
    public async Task<IActionResult> ToDocument(Guid id, CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var art = await _context.ToolArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (art is null) return NotFound();
        if (art.ToolSlug == "word-to-latex")
            return UnprocessableEntity(new { message = "Word documents open via import-review, not this endpoint." });

        var title = art.ToolSlug switch { "latex-table" => "Table", "doi-to-bibtex" => "Reference", _ => "From tool" };
        var doc = await _documents.CreateDocumentAsync(userId, new CreateDocumentDto { Title = title });

        // One block from the artifact: a real editable table from the grid input; the
        // BibTeX as a code block; otherwise the output as a paragraph.
        var block = art.ToolSlug switch
        {
            "latex-table" when art.Input is not null =>
                new CreateBlockDto("table", art.Input.RootElement, 0, null, null),
            "doi-to-bibtex" =>
                new CreateBlockDto("code", JsonSerializer.SerializeToElement(new { code = art.Output ?? "", language = "bibtex" }), 0, null, null),
            _ =>
                new CreateBlockDto("paragraph", JsonSerializer.SerializeToElement(new { text = art.Output ?? "" }), 0, null, null),
        };
        await _blocks.CreateBlockAsync(doc.Id, block);

        await RecordAsync(art.ToolSlug, userId, GetOrSetAnonId(), "signup", ct); // funnel: crossed into the product
        return Ok(new { documentId = doc.Id });
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
