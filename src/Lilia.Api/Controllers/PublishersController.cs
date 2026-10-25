using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// The publisher profiles the "Publish with…" picker offers (publisher templates phase 1, 25 Oct 2026):
/// venue, stage, class, options, bibliography style, template version, required metadata and the
/// publisher's guidelines. Set one on a document with PUT /api/documents/{id} { publisherProfile }.
/// </summary>
[ApiController]
[Route("api/publishers")]
[Authorize]
public class PublishersController(LiliaDbContext db, ILaTeXExportService latex) : ControllerBase
{
    /// <summary>How many documents the dashboard checks at most, newest first.</summary>
    public const int MaxDashboardDocuments = 50;

    [HttpGet]
    public IActionResult List() => Ok(new { catalogVersion = PublisherProfiles.CatalogVersion, profiles = PublisherProfiles.All });

    /// <summary>
    /// The compliance dashboard (phase 2, 25 Oct 2026): your own documents written for a publisher (a
    /// profile, or a publisher class), newest first, each with its venue and the publisher check's
    /// verdict: passed, warnings or errors, and the counts. The check reads each document and the LaTeX
    /// its export writes; nothing is compiled, so this answers at once.
    /// </summary>
    [HttpGet("documents")]
    public async Task<IActionResult> Documents(CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { error = "No valid authentication token provided" });

        var classes = PublisherClasses.All.ToList();
        var docs = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerId == userId && d.DeletedAt == null && !d.IsTemplate
                && (d.PublisherProfile != null || (d.LatexDocumentClass != null && classes.Contains(d.LatexDocumentClass))))
            .OrderByDescending(d => d.UpdatedAt)
            .Take(MaxDashboardDocuments)
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .AsSplitQuery()
            .ToListAsync(ct);

        var rows = docs.Select(d =>
        {
            var blocks = d.Blocks.OrderBy(b => b.SortOrder).ToList();
            var tex = latex.BuildSingleFileLatex(d, blocks, d.BibliographyEntries?.ToList() ?? [], new LaTeXExportOptions());
            var report = PublisherCompliance.Check(d, blocks, tex);
            var profile = PublisherProfiles.Find(d.PublisherProfile);
            return new
            {
                id = d.Id,
                title = d.Title,
                updatedAt = d.UpdatedAt,
                profile = profile?.Id,
                publisher = profile?.Publisher,
                venue = profile?.Venue,
                stage = profile?.Stage,
                documentClass = LaTeXPreambleBuilder.ResolveClassName(d),
                status = report.Status,
                errors = report.Findings.Count(f => f.Severity == "error"),
                warnings = report.Findings.Count(f => f.Severity == "warning"),
                // The worst finding, to say on the row why it isn't ready.
                first = report.Findings.OrderBy(f => f.Severity switch { "error" => 0, "warning" => 1, _ => 2 }).Select(f => f.Message).FirstOrDefault(),
            };
        }).ToList();

        return Ok(new { documents = rows, limit = MaxDashboardDocuments });
    }
}
