using Lilia.Api.Filters;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// A document's submissions and what changed since (publisher templates phase 3, 25 Oct 2026): every
/// submission package downloaded is kept as it was (document_submissions), and anyone who can read the
/// document sees them, the LaTeX diff since one, and the latexdiff marked-up PDF editors ask for with
/// a revision. Nothing here changes or deletes a submission.
/// </summary>
[ApiController]
[Route("api/documents/{docId:guid}/submissions")]
[Authorize]
public class SubmissionsController(
    LiliaDbContext db,
    ILaTeXExportService latex,
    ILaTeXRenderService render,
    ILogger<SubmissionsController> logger) : ControllerBase
{
    [HttpGet]
    [RequireDocumentAccess(Permissions.Read)]
    public async Task<IActionResult> List(Guid docId, CancellationToken ct)
    {
        var rows = await db.DocumentSubmissions.AsNoTracking()
            .Where(s => s.DocumentId == docId)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new { s.Id, s.CreatedAt, s.CreatedBy, s.Profile, s.ClassVersion, s.MainTexSha256 })
            .ToListAsync(ct);
        var names = await db.Users.AsNoTracking()
            .Where(u => rows.Select(r => r.CreatedBy).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Name ?? u.Email, ct);
        return Ok(rows.Select(r => new { r.Id, r.CreatedAt, by = names.GetValueOrDefault(r.CreatedBy), r.Profile, r.ClassVersion, sha256 = r.MainTexSha256 }));
    }

    /// <summary>The LaTeX diff between a kept submission ("latest" or its id) and the document now.</summary>
    [HttpGet("{submission}/diff")]
    [RequireDocumentAccess(Permissions.Read)]
    public async Task<IActionResult> Diff(Guid docId, string submission, CancellationToken ct)
    {
        var (kept, now) = await LoadAsync(docId, submission, ct);
        if (kept is null || now is null) return NotFound(new { error = "no_submission", message = "This document has no kept submission yet: download a submission package first." });
        var diff = await LatexDiff.CompareAsync(kept.MainTex, now.Value.MainTex, ct);
        return Ok(new
        {
            submission = new { kept.Id, kept.CreatedAt, kept.Profile },
            diff.Added, diff.Removed, diff.Truncated,
            hunks = diff.Hunks.Select(h => new { h.OldStart, h.NewStart, lines = h.Lines.Select(l => new { kind = l.Kind.ToString(), text = l.Text }) }),
        });
    }

    /// <summary>
    /// The marked-up PDF: latexdiff (TeX Live) between the kept submission's main.tex and now's, run in
    /// the TeX sandbox, compiled with the figures and both sets of references.
    /// </summary>
    [HttpGet("{submission}/marked-up.pdf")]
    [RequireDocumentAccess(Permissions.Read)]
    public async Task<IActionResult> MarkedUp(Guid docId, string submission, CancellationToken ct)
    {
        var (kept, now) = await LoadAsync(docId, submission, ct);
        if (kept is null || now is null) return NotFound(new { error = "no_submission", message = "This document has no kept submission yet: download a submission package first." });

        var dir = Path.Combine(Path.GetTempPath(), $"lilia-latexdiff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await System.IO.File.WriteAllTextAsync(Path.Combine(dir, "submitted.tex"), kept.MainTex, ct);
            await System.IO.File.WriteAllTextAsync(Path.Combine(dir, "now.tex"), now.Value.MainTex, ct);
            // --graphics-markup=none: latexdiff would otherwise re-\let \includegraphics to frame changed
            // images, which the TeX sandbox's guard refuses (it only lets \includegraphics name a file).
            var (exit, marked, stderr) = await Lilia.Engines.TexSafety.TexProcessRunner.RunAsync("latexdiff", "--graphics-markup=none submitted.tex now.tex", dir, 120);
            if (exit != 0 || !marked.Contains(@"\begin{document}", StringComparison.Ordinal))
            {
                logger.LogWarning("[Submissions] latexdiff failed for {DocId}: {Error}", docId, stderr.Length > 300 ? stderr[..300] : stderr);
                return UnprocessableEntity(new { error = "latexdiff_failed", message = "latexdiff could not compare the two versions." });
            }
            var doc = await db.Documents.AsNoTracking().Include(d => d.Blocks).FirstAsync(d => d.Id == docId, ct);
            var bib = LatexDiff.MergeBib(kept.ReferencesBib, now.Value.ReferencesBib);
            var pdf = await render.RenderToPdfTolerantAsync(marked, timeout: 120, engine: "pdflatex", materialiseAssets: async target =>
            {
                if (bib.Length > 0) await System.IO.File.WriteAllTextAsync(Path.Combine(target, "references.bib"), bib, ct);
                await latex.WriteFiguresAsync(doc.Blocks.ToList(), target);
            });
            var name = $"{System.Text.RegularExpressions.Regex.Replace(doc.Title, @"[^\w\- ]", "").Trim()}-changes.pdf";
            return File(pdf, "application/pdf", name.Length > 12 ? name : "changes.pdf");
        }
        catch (Lilia.Engines.TexSafety.UnsafeLatexException refused)
        {
            logger.LogWarning("[Submissions] marked-up PDF of {DocId} refused by the TeX guard: {Reason}", docId, refused.Message);
            return UnprocessableEntity(new { error = "unsafe_latex", message = refused.Message });
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    private async Task<(DocumentSubmission? Kept, (string MainTex, string? ReferencesBib)? Now)> LoadAsync(Guid docId, string submission, CancellationToken ct)
    {
        var q = db.DocumentSubmissions.AsNoTracking().Where(s => s.DocumentId == docId);
        var kept = submission == "latest"
            ? await q.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(ct)
            : Guid.TryParse(submission, out var id) ? await q.FirstOrDefaultAsync(s => s.Id == id, ct) : null;
        if (kept is null) return (null, null);
        var doc = await db.Documents.AsNoTracking()
            .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
            .Include(d => d.BibliographyEntries)
            .FirstOrDefaultAsync(d => d.Id == docId, ct);
        if (doc is null) return (kept, null);
        return (kept, latex.SubmissionSource(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries?.ToList() ?? []));
    }
}
