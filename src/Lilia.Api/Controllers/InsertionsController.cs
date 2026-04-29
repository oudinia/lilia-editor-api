using System.Text.Json;
using Lilia.Api.Services;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Per-document "what can I insert" feed. Drives the editor's insert panel
/// (and ⌘K palette extension): given a document's installed packages, list
/// every LaTeX command / environment that would actually render — i.e.
/// kernel tokens + tokens whose <c>package_slug</c> matches an installed
/// package, filtered to coverage levels that produce output.
///
/// Tokens whose <c>maps_to_block_type</c> is set are hidden because they
/// already have a first-class block button (heading, table, figure, …);
/// the insert panel surfaces *what's missing from the block toolbar*.
/// </summary>
[ApiController]
[Route("api/lilia/insertions")]
[Authorize]
public class InsertionsController : ControllerBase
{
    private readonly LiliaDbContext _db;
    private readonly IDocumentService _documentService;
    private readonly ILogger<InsertionsController> _logger;

    public InsertionsController(
        LiliaDbContext db,
        IDocumentService documentService,
        ILogger<InsertionsController> logger)
    {
        _db = db;
        _documentService = documentService;
        _logger = logger;
    }

    private string? GetUserId() => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// List installable insertions for a document.
    /// </summary>
    /// <param name="docId">Document to scope by — its installed packages.</param>
    [HttpGet]
    public async Task<ActionResult<List<InsertionDto>>> GetInsertions(
        [FromQuery] Guid docId,
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var document = await _documentService.GetDocumentAsync(docId, userId);
        if (document == null) return NotFound();

        // documents.latex_packages is a JSON array of { name, options? } objects.
        // Parse it once to a slug set; null/invalid → kernel-only result.
        var installedSlugs = ParseInstalledSlugs(document.LatexPackages);

        // Query joins latex_tokens with their (optional) latex_packages parent.
        // Filter:
        //   - kernel tokens (package_slug IS NULL)            ←  always available
        //   - or installed-package tokens                      ←  scoped to this doc
        //   - skip tokens that already have a block button     ←  no duplicate UX
        //   - skip 'unsupported' / 'none' coverage             ←  they don't render
        var rows = await _db.LatexTokens.AsNoTracking()
            .Where(t =>
                (t.PackageSlug == null || installedSlugs.Contains(t.PackageSlug))
                && t.MapsToBlockType == null
                && (t.CoverageLevel == "full" || t.CoverageLevel == "shimmed" || t.CoverageLevel == "partial"))
            .OrderBy(t => t.SemanticCategory ?? "zzz_uncategorised")
                .ThenBy(t => t.Name)
            .Select(t => new InsertionDto(
                t.Name,
                t.Kind,
                t.PackageSlug,
                t.SemanticCategory,
                t.CoverageLevel,
                t.ExpectsBody,
                t.Notes,
                t.InsertTemplate))
            .ToListAsync(ct);

        _logger.LogInformation(
            "[Insertions] doc={DocId} user={UserId} installedPkgs={PkgCount} rows={RowCount}",
            docId, userId, installedSlugs.Count, rows.Count);

        return Ok(rows);
    }

    /// <summary>
    /// Best-effort parse of <c>documents.latex_packages</c> — a JSON array of
    /// <c>{ "name": "amsmath", "options"?: "..." }</c>. Returns the bare slug
    /// set; logs nothing on parse failure (the column is user-editable).
    /// </summary>
    private static HashSet<string> ParseInstalledSlugs(string? latexPackagesJson)
    {
        if (string.IsNullOrWhiteSpace(latexPackagesJson)) return new HashSet<string>();
        try
        {
            using var doc = JsonDocument.Parse(latexPackagesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new HashSet<string>();

            var slugs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;
                if (entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                {
                    var name = n.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) slugs.Add(name);
                }
            }
            return slugs;
        }
        catch
        {
            return new HashSet<string>();
        }
    }
}

/// <summary>
/// Single insertion item — what the editor renders as a clickable row in
/// the insert panel and as an entry in the ⌘K palette.
/// </summary>
public sealed record InsertionDto(
    string Name,
    string Kind,
    string? PackageSlug,
    string? SemanticCategory,
    string CoverageLevel,
    bool ExpectsBody,
    string? Notes,
    /// <summary>
    /// Optional starter snippet for this token. <c>|CURSOR|</c> marks
    /// where the editor caret lands post-insert. NULL means the editor
    /// uses its default templates (see insertTokenIntoEditor).
    /// </summary>
    string? InsertTemplate);
