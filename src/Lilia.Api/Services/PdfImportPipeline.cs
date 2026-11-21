using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lilia.Api.Hubs;
using Lilia.Core.Entities;
using Lilia.Core.Interfaces;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lilia.Api.Services;

/// <summary>
/// The single PDF import's steps, for the bulk import slice (21 Nov 2026): the same page count, plan and credit checks,
/// charge and LaTeX project import as a PDF uploaded alone, so a PDF dropped in bulk costs and becomes the same. A
/// chunk's blocks are staged until the file is finished (21 Nov 2026), then made into its document in one go; a chunk
/// retried after that goes into the document, and an editor open on it is told, as for an app's change over MCP.
/// </summary>
public sealed class PdfImportPipeline(
    IJobService jobs,
    LiliaDbContext db,
    Lilia.Import.Services.ILatexProjectExtractor extractor,
    IHubContext<DocumentHub> documentHub,
    ILogger<PdfImportPipeline> logger,
    IEntitlementService? entitlements = null,
    Lilia.Api.Mcp.ConnectorAllowance? credits = null,
    IOptions<EntitlementOptions>? entitlementOptions = null,
    IStorageService? storage = null,
    IImportReviewService? review = null) : IPdfImportPipeline
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

    public async Task<PdfChunkStaged> StageLatexProjectAsync(
        string userId, string fileName, byte[] projectZip, string? sourcePdfKey,
        string assetKeyPrefix, string assetUrlPrefix, CancellationToken ct = default)
    {
        // The same steps as PlaceLatexProjectAsync up to the review session (21 Nov 2026, staging): the reading's LaTeX,
        // its cleanup (OcrMathText here, ScanImportCleanup with OcrMathAccents in the job), the LaTeX project import. Then
        // its rows are read rather than finalized into a document, and its files stored under the staging prefix.
        var started = Stopwatch.GetTimestamp();
        var extracted = extractor.Extract(projectZip);
        if (string.IsNullOrWhiteSpace(extracted.InlinedTex))
            throw new InvalidOperationException("Mathpix's reading of these pages has no LaTeX to import.");
        var latex = Lilia.Import.Services.OcrMathText.MathCommandsAsMaths(
            Lilia.Import.Services.MathpixFigures.AsFigureEnvironments(extracted.InlinedTex), out var wrapped);
        if (wrapped > 0)
            logger.LogInformation("[BulkImport] {File}: {Count} maths command(s) in Mathpix's text set back in maths", fileName, wrapped);
        var sessionId = await jobs.StagePdfChunkAsync(userId, fileName, latex, projectZip, sourcePdfKey, ct);
        var parseMs = ImportPlaceTimer.Since(started);

        var now = DateTime.UtcNow;
        // As the finalize: rows not reviewed count as approved; rejected ones (none, with no review) are left out.
        await db.ImportBlockReviews
            .Where(b => b.SessionId == sessionId && b.Status == "pending")
            .ExecuteUpdateAsync(b => b
                .SetProperty(x => x.Status, "approved")
                .SetProperty(x => x.ReviewedBy, userId)
                .SetProperty(x => x.ReviewedAt, now), ct);
        var rows = await db.ImportBlockReviews.AsNoTracking()
            .Where(b => b.SessionId == sessionId && (b.Status == "approved" || b.Status == "edited"))
            .OrderBy(b => b.SortOrder)
            .Select(b => new { b.Id, Type = b.CurrentType ?? b.OriginalType, Content = b.CurrentContent ?? b.OriginalContent, b.Depth })
            .ToListAsync(ct);

        // The reading's files (its figures' images), stored under the staging prefix: moved to the document's assets at
        // promotion. A failure deletes what was stored, so nothing is left behind.
        var figuresStarted = Stopwatch.GetTimestamp();
        var assets = new List<StagedAssetData>();
        var byName = new Dictionary<string, (Guid Id, string Url)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (storage is not null)
                foreach (var f in extracted.Files)
                {
                    var id = Guid.NewGuid();
                    var ext = Path.GetExtension(f.Path).ToLowerInvariant();
                    var name = $"{id:N}{ext}";
                    var key = assetKeyPrefix + name;
                    using (var stream = new MemoryStream(f.Bytes))
                        await storage.UploadAsync(key, stream, f.ContentType);
                    assets.Add(new StagedAssetData(id, name, f.Path, key, f.ContentType, f.Bytes.Length));
                    var url = assetUrlPrefix + name;
                    byName.TryAdd(f.Path, (id, url));
                    byName.TryAdd(Path.GetFileName(f.Path), (id, url));
                    byName.TryAdd(Path.GetFileNameWithoutExtension(f.Path), (id, url));
                }

            var blocks = rows.Select(r => new StagedBlockData(r.Id, r.Type,
                r.Type == "figure" ? FigureToStaged(r.Content, byName) : r.Content.RootElement.GetRawText(), r.Depth)).ToList();

            // The extractor's notices, as the finalize records them (the review's diagnostics).
            foreach (var notice in extracted.Notices)
                db.ImportDiagnostics.Add(new ImportDiagnostic
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    Severity = "warning",
                    Category = "auto_shimmed",
                    Message = notice,
                    CreatedAt = now,
                });
            if (extracted.Notices.Count > 0) await db.SaveChangesAsync(ct);
            await db.ImportReviewSessions.Where(x => x.Id == sessionId)
                .ExecuteUpdateAsync(x => x.SetProperty(v => v.Status, "imported").SetProperty(v => v.UpdatedAt, now), ct);
            var figuresMs = ImportPlaceTimer.Since(figuresStarted);

            var warnings = await db.ImportDiagnostics.AsNoTracking()
                .CountAsync(d => d.SessionId == sessionId && !d.Dismissed && (d.Severity == "warning" || d.Severity == "error"), ct);
            logger.LogInformation("[BulkImport] {File}: {Blocks} block(s) and {Assets} file(s) staged, {Warnings} warning(s)",
                fileName, blocks.Count, assets.Count, warnings);
            return new PdfChunkStaged(sessionId, blocks, assets, PreambleOf(latex), warnings, ToMs(parseMs), ToMs(figuresMs));
        }
        catch
        {
            await DeleteQuietlyAsync(assets.Select(a => a.StorageKey));
            throw;
        }
    }

    /// <summary>A figure's content with its <c>src</c> pointing at its staged file (by path, name, or name without extension).</summary>
    private static string FigureToStaged(JsonDocument content, Dictionary<string, (Guid Id, string Url)> byName)
    {
        var raw = content.RootElement.GetRawText();
        if (JsonNode.Parse(raw) is not JsonObject node || node["src"]?.GetValueKind() != JsonValueKind.String) return raw;
        var src = node["src"]!.GetValue<string>();
        if (string.IsNullOrEmpty(src)) return raw;
        if (!byName.TryGetValue(src, out var hit)
            && !byName.TryGetValue(Path.GetFileName(src), out hit)
            && !byName.TryGetValue(Path.GetFileNameWithoutExtension(src), out hit))
            return raw;
        node["src"] = hit.Url;
        node["stagedAssetId"] = hit.Id.ToString();
        return node.ToJsonString();
    }

    /// <summary>The LaTeX before <c>\begin{document}</c>, or null when there is none.</summary>
    internal static string? PreambleOf(string latex)
    {
        var at = latex.IndexOf(@"\begin{document}", StringComparison.Ordinal);
        return at > 0 && !string.IsNullOrWhiteSpace(latex[..at]) ? latex[..at] : null;
    }

    private async Task DeleteQuietlyAsync(IEnumerable<string> keys)
    {
        if (storage is null) return;
        foreach (var key in keys)
        {
            try { await storage.DeleteAsync(key); }
            catch (Exception ex) { logger.LogWarning(ex, "[BulkImport] Could not delete staged file {Key}", key); }
        }
    }

    public async Task ApplyStagedPreambleAsync(Guid documentId, string ownerId, string? preamble, string? extraMacros, CancellationToken ct = default)
    {
        // As a single import's finalize (ApplyLatexSourceAsync): class, packages, macros, page setup and venue, from the
        // preamble with an empty body. Then later chunks' macros the preamble lacks.
        if (!string.IsNullOrWhiteSpace(preamble) && review is not null)
            await review.ApplyLatexSourceAsync(documentId, ownerId, preamble + "\n\\begin{document}\n\\end{document}\n", "bulk import", ct);
        if (string.IsNullOrWhiteSpace(extraMacros)) return;
        var current = await db.Documents.AsNoTracking().Where(d => d.Id == documentId).Select(d => d.CustomPreamble).FirstOrDefaultAsync(ct);
        if (LatexPreambleExtractor.MissingDefinitions(current, extraMacros) is { } missing)
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE documents SET custom_preamble =
                    CASE WHEN custom_preamble IS NULL OR custom_preamble = '' THEN {missing}
                         ELSE custom_preamble || E'\n' || {missing} END
                WHERE id = {documentId}", ct);
    }

    public string? MissingDefinitions(string? have, string? latex) => LatexPreambleExtractor.MissingDefinitions(have, latex);

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
