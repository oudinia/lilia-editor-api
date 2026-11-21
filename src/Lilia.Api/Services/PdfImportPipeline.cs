using System.Diagnostics;
using Lilia.Api.Hubs;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// The single PDF import's steps, for the bulk import slice (21 Nov 2026): the same page count, plan and credit checks,
/// charge and LaTeX project import as a PDF uploaded alone, so a PDF dropped in bulk costs and becomes the same. A
/// chunk's blocks go into the file's growing document; an editor open on it is told, as for an app's change over MCP.
/// </summary>
public sealed class PdfImportPipeline(
    IJobService jobs,
    LiliaDbContext db,
    Lilia.Import.Services.ILatexProjectExtractor extractor,
    IHubContext<DocumentHub> documentHub,
    ILogger<PdfImportPipeline> logger,
    IEntitlementService? entitlements = null,
    Lilia.Api.Mcp.ConnectorAllowance? credits = null,
    IOptions<EntitlementOptions>? entitlementOptions = null) : IPdfImportPipeline
{
    public int? CountPages(string pdfPath) => PdfPages.ExactFile(pdfPath);

    public async Task EnsureCanImportPdfPagesAsync(string userId, int pages, bool newDocument = true, CancellationToken ct = default)
    {
        // Fails closed, as the single PDF import: a PDF costs Lilia money per page.
        if (entitlements is null)
            throw new InvalidOperationException("PDF import is not available: plans can't be checked.");
        if (newDocument) await entitlements.EnsureCanImportAsync(userId, ct);
        await entitlements.EnsureCanImportPdfAsync(userId, pages, ct);
        if (credits is not null) await credits.EnsureCreditsForPagesAsync(userId, pages, image: false, ct);
    }

    public Task ChargePdfPagesAsync(string userId, int pages, CancellationToken ct = default) =>
        jobs.ChargePdfPagesAsync(userId, pages);

    public async Task<PdfChunkPlacement> PlaceLatexProjectAsync(
        string userId, string fileName, Guid documentId, Guid? placeholderBlockId, bool applyPreamble,
        byte[] projectZip, string? sourcePdfKey, CancellationToken ct = default)
    {
        // Timed by part (the bulk import timings, 21 Nov 2026): this method's extract counts as parse; JobService and
        // ImportReviewService add the staging, the finalize and the figures to the ambient timer.
        var timer = new ImportPlaceTimer();
        ImportPlaceTimer.Current = timer;
        var started = Stopwatch.GetTimestamp();

        // As MathpixPdfImportService reads a tex.zip: the flattened .tex, every bare \includegraphics set in a figure
        // environment so it is read as a figure block (20 Nov 2026).
        var extracted = extractor.Extract(projectZip);
        if (string.IsNullOrWhiteSpace(extracted.InlinedTex))
            throw new InvalidOperationException("Mathpix's reading of these pages has no LaTeX to import.");
        // A maths command Mathpix left in the text is set back in maths (OcrMathText, 21 Nov 2026).
        var latex = Lilia.Import.Services.OcrMathText.MathCommandsAsMaths(
            Lilia.Import.Services.MathpixFigures.AsFigureEnvironments(extracted.InlinedTex), out var wrapped);
        var extractMs = ImportPlaceTimer.Since(started);
        if (wrapped > 0)
            logger.LogInformation("[BulkImport] {File}: {Count} maths command(s) in Mathpix's text set back in maths", fileName, wrapped);
        var (sessionId, blockIds) = await jobs.ImportPdfChunkIntoDocumentAsync(
            userId, fileName, documentId, placeholderBlockId, applyPreamble, latex, projectZip, sourcePdfKey, ct);
        var warnings = await db.ImportDiagnostics.AsNoTracking()
            .CountAsync(d => d.SessionId == sessionId && !d.Dismissed && (d.Severity == "warning" || d.Severity == "error"), ct);
        logger.LogInformation("[BulkImport] {File}: {Blocks} block(s) placed into {DocumentId}, {Warnings} warning(s)",
            fileName, blockIds.Count, documentId, warnings);
        ImportPlaceTimer.Current = null;
        return new PdfChunkPlacement(sessionId, blockIds, warnings,
            ParseMs: ToMs(extractMs + timer.ParseMs),
            FiguresMs: ToMs(timer.FiguresMs),
            WriteMs: ToMs(Math.Max(0, timer.FinalizeMs - timer.FiguresMs)));
    }

    private static int ToMs(long ms) => (int)Math.Clamp(ms, 0, int.MaxValue);

    public bool IsStaff(string userId) =>
        !string.IsNullOrEmpty(userId) && entitlementOptions?.Value.StaffUserIds.Contains(userId, StringComparer.Ordinal) == true;

    public async Task NotifyDocumentChangedAsync(Guid documentId, IReadOnlyList<Guid> blockIds, CancellationToken ct = default)
    {
        // The editor's live room (useLiveDocumentChanges): it reloads the blocks, or, with typing not yet saved, offers to.
        try
        {
            await documentHub.Clients.Group($"doc-{documentId}").SendAsync("DocumentChangedExternally", new
            {
                source = "bulk-import",
                app = "Bulk import",
                tool = "place_pages",
                blockIds = blockIds.Select(b => b.ToString()).ToArray(),
                undoVersionId = (string?)null,
                at = DateTime.UtcNow,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "[BulkImport] Could not tell the open editor of {DocumentId}", documentId);
        }
    }
}
