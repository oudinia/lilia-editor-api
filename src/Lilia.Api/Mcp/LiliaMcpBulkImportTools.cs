using System.ComponentModel;
using Lilia.Api.Features.BulkImport.Contracts;
using Lilia.Api.Features.BulkImport.Services;
using Lilia.Api.Services;
using ModelContextProtocol.Server;

namespace Lilia.Api.Mcp;

/// <summary>
/// Bulk PDF import through the connector (the owner, 21 Nov 2026: "the MCP connector should also start a bulk import and
/// check its progress, on the same jobs" as the web; asked again 22 Nov). The same batches, files and chunks as
/// <c>/import/bulk</c>: a batch the connector starts shows on the web page, and one started on the web is read here.
/// PDFs the app has go in as base64 (a few per call, 10 MB each); larger files go through the drop page's link.
/// </summary>
[McpServerToolType]
public sealed class LiliaMcpBulkImportTools(
    IHttpContextAccessor http,
    IConfiguration config,
    BulkImportService bulk,
    ILogger<LiliaMcpBulkImportTools> logger)
{
    /// <summary>PDFs one call takes: base64 grows a file by a third, and a request has to stay a request.</summary>
    internal const int MaxFilesPerCall = 5;

    /// <summary>Base64 for 10 MB, as import_document.</summary>
    private const int MaxBase64Chars = 14_000_000;

    private System.Security.Claims.ClaimsPrincipal? User => http.HttpContext?.User;

    private string UserId =>
        User?.FindFirst("sub")?.Value
        ?? User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("Not signed in.");

    /// <summary>A PDF the app sends: its file name and its bytes in base64.</summary>
    public sealed record BulkImportUpload(
        [property: Description("The file name, e.g. notebook-07.pdf.")] string Filename,
        [property: Description("The PDF, base64, up to 10 MB.")] string Content);

    [McpServerTool(Name = "start_bulk_import", Title = "Start a bulk PDF import", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Convert many PDFs at once (scanned notebooks, handwritten notes, papers), each into its own document, in the background: the author can leave and come back. Two ways: (1) send the PDFs you have as files (up to 5 per call, 10 MB each; call again with the same batchId to add more, up to 50); (2) send no files to get the link to Lilia's bulk drop page, where the author drops up to 50 PDFs of 95 MB each. Each page counts against the author's plan's monthly PDF pages. Then call bulk_import_status for progress: each file is read 10 pages at a time, and its document appears when it is finished.")]
    public async Task<object> StartBulkImport(
        [Description("The PDFs to convert. Omit to get the drop page's link instead.")] BulkImportUpload[]? files = null,
        [Description("Add to this bulk import (from an earlier start_bulk_import); omit to start a new one.")] string? batchId = null)
    {
        if (!LiliaMcpWriteTools.AppMayWrite(User, LiliaMcpWriteTools.RequireWriteScope(config)))
            return new { error = LiliaMcpWriteTools.ReconnectMessage };
        var origin = McpOAuth.PublicOrigin(config);
        if (files is not { Length: > 0 })
            return new
            {
                url = $"{origin}/import/bulk",
                note = "Give the author this link (signed in to Lilia): they drop up to 50 PDFs there, 95 MB each. Then call bulk_import_status with no batchId to see the import and its progress.",
            };
        if (files.Length > MaxFilesPerCall)
            return new { error = $"Send at most {MaxFilesPerCall} PDFs per call; call again with the batchId for the rest.", category = "invalid" };

        Guid batch;
        if (string.IsNullOrWhiteSpace(batchId)) batch = await bulk.CreateBatchAsync(UserId);
        else if (!Guid.TryParse(batchId.Trim(), out batch)) return new { error = "batchId is not a bulk import id.", category = "invalid" };

        var results = new List<object>();
        var added = 0;
        foreach (var f in files)
        {
            var name = string.IsNullOrWhiteSpace(f.Filename) ? "document.pdf" : f.Filename.Trim();
            if (string.IsNullOrEmpty(f.Content) || f.Content.Length > MaxBase64Chars)
            {
                results.Add(new { name, added = false, error = "Each PDF is up to 10 MB here; give the author the drop page's link (start_bulk_import with no files) for larger ones." });
                continue;
            }
            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.Content); }
            catch (FormatException)
            {
                results.Add(new { name, added = false, error = "The content is not base64." });
                continue;
            }
            try
            {
                await using var stream = new MemoryStream(bytes, writable: false);
                var r = await bulk.AddFileAsync(UserId, batch, stream, name);
                if (r.Refusal == AddFileRefusal.BatchNotFound)
                    return new { error = "No such bulk import for this author: check the batchId, or start a new one without it.", category = "invalid" };
                if (r.File is { } file)
                {
                    added++;
                    results.Add(new { name = file.Name, added = true, fileId = file.FileId, pages = file.Pages });
                }
                else results.Add(new { name, added = false, error = r.Message ?? r.Refusal.ToString() });
            }
            catch (QuotaExceededException limit)
            {
                // The plan, not the request: the rest would be refused the same way.
                results.Add(new { name, added = false, error = limit.Message, category = "plan-limit" });
                break;
            }
        }
        logger.LogInformation("[Mcp] start_bulk_import: {Added}/{Sent} PDFs into batch {BatchId}", added, files.Length, batch);
        return new
        {
            batchId = batch,
            files = results,
            url = $"{origin}/import/bulk/{batch}",
            note = "Call bulk_import_status with this batchId for progress (every 20 to 30 seconds is plenty). The author can follow it at the url too.",
        };
    }

    [McpServerTool(Name = "bulk_import_status", Title = "Check a bulk import", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Where a bulk PDF import stands, on the web or through the connector: per file, its pages read out of its pages, pages that couldn't be read, the warnings to check against the scan, and the document once it is finished. With no batchId: the author's latest bulk imports, newest first.")]
    public async Task<object> BulkImportStatus(
        [Description("The bulk import's id (from start_bulk_import or this tool's list). Omit for the latest ones.")] string? batchId = null)
    {
        var origin = McpOAuth.PublicOrigin(config);
        if (string.IsNullOrWhiteSpace(batchId))
        {
            var list = await bulk.ListAsync(UserId);
            return new
            {
                imports = list.Take(5).Select(b => new
                {
                    batchId = b.BatchId,
                    status = b.Status,
                    createdAt = b.CreatedAt,
                    pages = b.PagesTotal,
                    pagesDone = b.PagesDone,
                    files = b.FilesTotal,
                    filesDone = b.FilesDone,
                    filesFailed = b.FilesFailed,
                    url = $"{origin}/import/bulk/{b.BatchId}",
                }),
                total = list.Count,
            };
        }
        if (!Guid.TryParse(batchId.Trim(), out var id)) return new { error = "batchId is not a bulk import id.", category = "invalid" };
        if (await bulk.GetAsync(UserId, id) is not { } batch)
            return new { error = "No such bulk import for this author.", category = "invalid" };
        return new
        {
            batchId = batch.BatchId,
            status = batch.Status,
            pages = batch.PagesTotal,
            pagesDone = batch.PagesDone,
            pagesFailed = batch.PagesFailed,
            files = batch.Files.Select(File),
            url = $"{origin}/import/bulk/{batch.BatchId}",
            note = batch.Files.Any(f => f.Warnings > 0)
                ? "Some files have warnings (a symbol or a page the reader was unsure of): tell the author to check them against the scan, from the url's file rows."
                : null,
        };

        object File(BulkImportFileDto f) => new
        {
            fileId = f.FileId,
            name = f.Name,
            status = f.Status,
            pages = f.Pages,
            pagesDone = f.PagesDone,
            pagesFailed = f.PagesFailed,
            warnings = f.Warnings,
            precision = f.Precision?.Score,
            documentId = f.DocumentId,
            documentUrl = f.DocumentId is { } d ? $"{origin}/d/{d}" : null,
            error = f.Error,
        };
    }
}
