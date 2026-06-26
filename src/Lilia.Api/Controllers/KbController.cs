using Lilia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lilia.Api.Controllers;

/// <summary>
/// Public read API over the Lilia knowledge base — the screenshot-free, per-tool /
/// per-concept help catalog. Two audiences:
///  • the AI ("Ask Lilia") calls <c>search</c> / <c>tool/{slug}</c> to discover the
///    right article and point the author to it;
///  • the public site lists and renders articles.
/// Content is help/marketing — anonymous, read-only.
/// </summary>
[ApiController]
[Route("api/kb")]
[AllowAnonymous]
public sealed class KbController : ControllerBase
{
    private readonly IKbService _kb;
    public KbController(IKbService kb) => _kb = kb;

    /// <summary>List articles, optionally filtered by tool or audience.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<KbArticleSummary>> List(
        [FromQuery] string? tool, [FromQuery] string? audience, [FromQuery] int limit = 100, CancellationToken ct = default)
        => await _kb.ListAsync(tool, audience, limit, ct);

    /// <summary>Full-text search — the AI's discovery entry point. Ranked by relevance.</summary>
    [HttpGet("search")]
    public async Task<IReadOnlyList<KbArticleSummary>> Search(
        [FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
        => await _kb.SearchAsync(q ?? string.Empty, limit, ct);

    /// <summary>Articles for one tool (e.g. <c>doi-to-bibtex</c>).</summary>
    [HttpGet("tool/{toolSlug}")]
    public async Task<IReadOnlyList<KbArticleSummary>> ForTool(string toolSlug, CancellationToken ct = default)
        => await _kb.ForToolAsync(toolSlug, ct);

    /// <summary>One full article (with body) by slug.</summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<KbArticleDetail>> Get(string slug, CancellationToken ct = default)
    {
        var article = await _kb.GetAsync(slug, ct);
        return article is null ? NotFound() : Ok(article);
    }
}
