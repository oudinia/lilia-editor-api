using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Core.DTOs;
using Lilia.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Controllers;

/// <summary>
/// The file side of the connector's import and export (<see cref="ConnectorTransfers"/>): the app
/// hands the author a link, and the file goes between the author's browser and Lilia, never
/// through the conversation (the user, 12 Oct: import and export "must not consume too many tokens").
/// </summary>
[ApiController]
[Route("api/connector")]
public class ConnectorFilesController(
    ConnectorTransfers transfers,
    IDocumentService documents,
    IDocumentExportService exports,
    ILaTeXExportService latex,
    IJobService jobs,
    LiliaDbContext db,
    Lilia.Api.Mcp.CheckMemoStore checks,
    Lilia.Engines.IRenderService render,
    ILaTeXRenderService latexRender,
    ILogger<ConnectorFilesController> logger) : ControllerBase
{
    private string? UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// The download behind <c>export_document</c>'s link. No sign-in: the link is the permission,
    /// short-lived and unguessable; the author's access to the document is checked again here.
    /// </summary>
    [HttpGet("files/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(string token)
    {
        if (transfers.Export(token) is not { } t)
            return Explain(404, "This download link has expired or is not valid. Ask the app for a new one.");
        var doc = await documents.GetDocumentAsync(t.DocumentId, t.UserId);
        if (doc is null) return Explain(404, "This document is no longer available.");
        var name = FileName(doc.Title);
        logger.LogInformation("[Connector] {Format} download of {DocId} for {UserId}", t.Format, t.DocumentId, t.UserId);
        try
        {
            switch (t.Format)
            {
                case "pdf":
                    // A version that doesn't compile gets no PDF (MCP-110: a link made before the check
                    // served one, the broken command silently dropped by the lenient renderer). The
                    // current check decides, or a strict compile when there is none (cached by source).
                    if (await CompileErrorAsync(doc) is { } compileError)
                        return Explain(422, $"Lilia could not make a PDF of this document: it does not compile. {compileError} Open it in Lilia and run Validate, or ask the app to check it.");
                    var (pdf, _) = await exports.ExportToPdfWithEngineAsync(t.DocumentId, "auto");
                    if (pdf is null || pdf.Length < 100 || !pdf.AsSpan(0, 4).SequenceEqual("%PDF"u8))
                        return Explain(422, "Lilia could not make a PDF of this document: it does not compile. Open it in Lilia and run Validate, or ask the app to check it.");
                    return File(pdf, "application/pdf", $"{name}.pdf");
                case "docx":
                    return File(await exports.ExportToDocxAsync(t.DocumentId),
                        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{name}.docx");
                case "zip":
                    return File(await latex.ExportToZipAsync(t.DocumentId, new LaTeXExportOptions()), "application/zip", $"{name}.zip");
                default:
                    var full = await db.Documents.AsNoTracking()
                        .Include(d => d.Blocks.OrderBy(b => b.SortOrder))
                        .Include(d => d.BibliographyEntries)
                        .FirstOrDefaultAsync(d => d.Id == t.DocumentId);
                    if (full is null) return NotFound();
                    var tex = latex.BuildSingleFileLatex(full, full.Blocks.OrderBy(b => b.SortOrder).ToList(),
                        full.BibliographyEntries?.ToList() ?? new(), new LaTeXExportOptions());
                    return File(System.Text.Encoding.UTF8.GetBytes(tex), "application/x-tex", $"{name}.tex");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Connector] {Format} export of {DocId} failed", t.Format, t.DocumentId);
            return Explain(500, "Lilia could not produce this file. Try again, or export it from the editor.");
        }
    }

    private async Task<string?> CompileErrorAsync(Lilia.Core.DTOs.DocumentDto doc)
    {
        if (checks.Current(doc.Id, doc.UpdatedAt) is { } memo)
            return memo.Compiles ? null : Trimmed(memo.Error);
        var source = await render.RenderToLatexAsync(doc.Id);
        var result = await latexRender.ValidateAsync(source, Lilia.Api.Mcp.LiliaMcpTools.EngineFor(source, doc.LatexEngine));
        return result.Valid ? null : Trimmed(result.Error);

        static string Trimmed(string? error) =>
            string.IsNullOrWhiteSpace(error) ? "" : (error.Length > 300 ? error[..300] + "…" : error).Trim();
    }

    public sealed record ImportTicketView(string Status, string App, DateTime ExpiresAt, Guid? DocumentId, string? Title, string? Error);

    /// <summary>The editor's drop page reads its ticket: for the signed-in author only.</summary>
    [HttpGet("imports/{token}")]
    [Authorize]
    public IActionResult ImportTicket(string token) =>
        UserId is { } userId && transfers.Import(token, userId) is { } t
            ? Ok(new ImportTicketView(t.Status, t.App, t.ExpiresAt, t.DocumentId, t.Title, t.Error))
            : NotFound(new { error = "This import link has expired or belongs to someone else." });

    public sealed record ImportFile(string Content, string Format, string Filename);

    /// <summary>
    /// The drop page sends the file here (text for .tex and .lml, base64 for .docx). It goes through
    /// Lilia's importer without the review step, as one new document of the author's.
    /// </summary>
    [HttpPost("imports/{token}")]
    [Authorize]
    [RequestSizeLimit(40_000_000)]
    public async Task<IActionResult> Import(string token, [FromBody] ImportFile file)
    {
        if (UserId is not { } userId || transfers.Import(token, userId) is not { } t)
            return NotFound(new { error = "This import link has expired or belongs to someone else." });
        if (t.Status == "done") return Conflict(new { error = "This link was already used.", documentId = t.DocumentId });
        var format = (file.Format ?? "").Trim().ToUpperInvariant();
        if (format is not ("LATEX" or "DOCX" or "LML"))
            return BadRequest(new { error = "Lilia imports .tex, .docx and .lml files here." });
        try
        {
            var result = await jobs.CreateImportJobFromBase64Async(userId,
                new ImportRequestDto(file.Content, format, file.Filename, SkipReview: true));
            if (result.Document is null)
                throw new InvalidOperationException(result.Job?.ErrorMessage ?? "the importer produced no document");
            t.Status = "done";
            t.DocumentId = result.Document.Id;
            t.Title = result.Document.Title;
            return Ok(new { documentId = result.Document.Id, title = result.Document.Title });
        }
        catch (QuotaExceededException limit)
        {
            t.Status = "failed";
            t.Error = limit.Message;
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = limit.Message, category = "plan-limit" });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Connector] import of {File} failed for {UserId}", file.Filename, userId);
            t.Status = "failed";
            t.Error = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            return BadRequest(new { error = $"Lilia could not import this file: {t.Error}" });
        }
    }

    /// <summary>
    /// The link is opened in a browser, often on a phone: a plain sentence reads better than JSON.
    /// </summary>
    private ContentResult Explain(int status, string message) =>
        new() { StatusCode = status, ContentType = "text/plain; charset=utf-8", Content = message };

    private static string FileName(string? title)
    {
        var clean = new string((title ?? "document").Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' ? c : '_').ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? "document" : clean.Length > 80 ? clean[..80] : clean;
    }
}
