namespace Lilia.Api.Services;

/// <summary>
/// A finished bulk import's document compiled once, to measure it (the bulk import timings, 21 Nov 2026): the editor's
/// own compile path (<see cref="IPreviewRenderService.RenderPdfAsync"/>: Typst for a plain document, else LaTeX with the
/// engine the document would use). Its TeX and Typst runs wait for the machine's compile slots like any other caller,
/// so a bulk compile never takes more than its share; the bulk import's compile queue runs one at a time.
/// </summary>
public sealed class BulkImportDocumentCompiler(IPreviewRenderService preview, ILogger<BulkImportDocumentCompiler> logger) : IBulkImportCompiler
{
    public async Task<BulkImportCompileResult> CompileAsync(Guid documentId, CancellationToken ct = default)
    {
        try
        {
            var result = await preview.RenderPdfAsync(documentId, ct);
            return result.Pdf is { Length: > 0 }
                ? new BulkImportCompileResult(true, result.Engine, null)
                : new BulkImportCompileResult(false, result.Engine, "The compile made an empty PDF.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogInformation(ex, "[BulkImport] Document {DocumentId} didn't compile (measured, nothing else changes)", documentId);
            return new BulkImportCompileResult(false, null, FirstLine(ex.Message));
        }
    }

    /// <summary>An error's first line that says something, at most 500 characters.</summary>
    internal static string FirstLine(string? message)
    {
        var line = (message ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "The compile failed.";
        return line.Length > 500 ? line[..500] : line;
    }
}
