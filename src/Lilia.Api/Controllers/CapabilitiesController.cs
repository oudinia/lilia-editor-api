using Lilia.Api.Services.Capabilities;
using Lilia.Core.Capabilities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// Pre-flight: what will this document need, and does the chosen target
/// provide it?
///
/// <para><b>Read-only, and nothing else calls it.</b> No render path, no export
/// path and no engine choice goes through here. It answers a question that
/// could previously only be answered by compiling and seeing what happened.</para>
///
/// <para>That is the whole reason phase 3 exists. Every other diagnostic in
/// this system reports after the fact — <c>X-Render-Engine</c> names a fallback
/// that already occurred, <c>silent_fallback</c> telemetry records a compile
/// that already failed, font coverage answers about a document that already
/// exists. This answers first.</para>
/// </summary>
[ApiController]
[Route("api/documents/{docId:guid}/capabilities")]
[Authorize]
public class CapabilitiesController(
    LiliaDbContext context,
    CapabilityResolver resolver,
    ILogger<CapabilitiesController> logger) : ControllerBase
{
    private string? GetUserId() =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <param name="docId">Document to inspect.</param>
    /// <param name="engine">
    /// pdflatex | xelatex | lualatex | typst. Rejected rather than defaulted
    /// when unrecognised: silently falling back to pdflatex would answer about
    /// the target least able to render what people ask about, and the caller
    /// would have no way to tell they had been answered about the wrong thing.
    /// </param>
    [HttpGet]
    public async Task<ActionResult<CapabilityReportDto>> Get(
        Guid docId,
        [FromQuery] string engine = "pdflatex",
        CancellationToken ct = default)
    {
        var userId = GetUserId();
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        if (!RenderTargets.TryParse(engine, out var target))
        {
            return BadRequest(new
            {
                error = $"Unknown engine '{engine}'",
                supported = RenderTargets.All.Select(t => t.ToWireName()),
            });
        }

        var document = await context.Documents
            .Where(d => d.Id == docId && d.OwnerId == userId)
            .Select(d => new { d.Id, d.FontFamily, d.LatexDocumentClass })
            .FirstOrDefaultAsync(ct);
        if (document is null) return NotFound();

        var blocks = await context.Blocks
            .Where(b => b.DocumentId == docId)
            .OrderBy(b => b.SortOrder)
            .Select(b => b.Content)
            .ToListAsync(ct);

        var requirements = RequirementExtractor.Extract(
            blocks.Where(c => c is not null).Select(c => c!.RootElement),
            document.FontFamily,
            document.LatexDocumentClass);

        var report = await resolver.ResolveAsync(requirements, target, ct);

        logger.LogInformation(
            "[Capabilities] {DocId} against {Target}: {Summary}; {ProblemCount} to report, " +
            "{UnavailableCount} provider(s) unavailable",
            docId, target.ToWireName(), RequirementExtractor.Summarise(requirements),
            report.Problems.Count, report.UnavailableProviders.Count);

        return Ok(new CapabilityReportDto(
            target.ToWireName(),
            report.IsFullySatisfied,
            RequirementExtractor.Summarise(requirements),
            requirements.Count,
            report.UnavailableProviders,
            [.. report.Problems.Select(p => new CapabilityProblemDto(
                p.Requirement.Key,
                p.Requirement.Describe(),
                p.Support.ToString().ToLowerInvariant(),
                p.Detail,
                p.Alternatives,
                [.. p.Verdicts.Select(v => v.Source)]))]));
    }
}

/// <param name="Engine">What this was resolved against.</param>
/// <param name="FullySatisfied">
/// True only when everything resolved cleanly <b>and</b> every provider could
/// be consulted. "Nothing known to be wrong" is not "nothing wrong".
/// </param>
/// <param name="Summary">One line describing what the document needs.</param>
/// <param name="RequirementCount">How many distinct requirements were checked.</param>
/// <param name="UnavailableProviders">
/// Catalogues that could not be reached. Present in the payload rather than
/// only in a log, because the caller is the only one who can tell a clean
/// report from a clean report with a silent provider behind it.
/// </param>
/// <param name="Problems">Everything not satisfied, worst first.</param>
public sealed record CapabilityReportDto(
    string Engine,
    bool FullySatisfied,
    string Summary,
    int RequirementCount,
    IReadOnlyList<string> UnavailableProviders,
    IReadOnlyList<CapabilityProblemDto> Problems);

/// <param name="Key">Stable identity, e.g. <c>codepoint:U+4E2D</c>.</param>
/// <param name="Requirement">Human-readable form.</param>
/// <param name="Support">full | shimmed | partial | none | impossible | unknown.</param>
/// <param name="Detail">What to do about it, when a provider offered something.</param>
/// <param name="Alternatives">Other targets or fonts that would satisfy it.</param>
/// <param name="Sources">Which catalogues answered — the catalogues are hand-authored.</param>
public sealed record CapabilityProblemDto(
    string Key,
    string Requirement,
    string Support,
    string? Detail,
    IReadOnlyList<string> Alternatives,
    IReadOnlyList<string> Sources);
