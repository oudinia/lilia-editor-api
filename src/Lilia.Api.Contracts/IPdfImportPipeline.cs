namespace Lilia.Api.Services;

/// <summary>
/// What the bulk import slice (<c>Lilia.Features.BulkImport</c>, 21 Nov 2026) uses of the single PDF import, so a PDF
/// dropped in bulk is counted, checked, charged and imported exactly as one uploaded alone: the same page count, the same
/// plan and credit checks, the same charge, and the same LaTeX project import (the LaTeX import, the cleanup of Mathpix's
/// reading, its figures stored as the document's assets). The difference: a chunk's blocks go into the file's existing
/// document, in place of the chunk's placeholder, rather than making a document of their own. Implemented in Lilia.Api
/// over <c>PdfPages</c>, <c>IEntitlementService</c>, the connector's credits, <c>JobService</c> and
/// <c>ImportReviewService</c>.
/// </summary>
public interface IPdfImportPipeline
{
    /// <summary>The exact pages of the PDF at <paramref name="pdfPath"/> (pdfinfo, else the page tree), or null.</summary>
    int? CountPages(string pdfPath);

    /// <summary>
    /// Refuses, with a <see cref="QuotaExceededException"/>, an import of <paramref name="pages"/> PDF pages the author's
    /// plan or credits don't cover: the monthly imports and documents (when <paramref name="newDocument"/>), the plan's PDF
    /// pages, the site's, and the credits the pages cost. Staff are not limited. Throws an
    /// <see cref="InvalidOperationException"/> when plans can't be checked (fails closed, as the single import).
    /// </summary>
    Task EnsureCanImportPdfPagesAsync(string userId, int pages, bool newDocument = true, CancellationToken ct = default);

    /// <summary>Charges pages Mathpix read: against the plan's monthly PDF pages and the site's, and their credits.</summary>
    Task ChargePdfPagesAsync(string userId, int pages, CancellationToken ct = default);

    /// <summary>
    /// A chunk's LaTeX project zip (Mathpix's tex.zip) imported as a PDF's tex.zip is, its blocks put into
    /// <paramref name="documentId"/> in place of <paramref name="placeholderBlockId"/> (appended when that block is gone),
    /// its figures stored as the document's assets. With <paramref name="applyPreamble"/> its preamble and page setup go
    /// onto the document as the single import's do; otherwise only the macros the document lacks are added.
    /// <paramref name="sourcePdfKey"/> is the original PDF in storage, kept as the import's source file.
    /// </summary>
    Task<PdfChunkPlacement> PlaceLatexProjectAsync(
        string userId, string fileName, Guid documentId, Guid? placeholderBlockId, bool applyPreamble,
        byte[] projectZip, string? sourcePdfKey, CancellationToken ct = default);

    /// <summary>
    /// Tells an editor open on <paramref name="documentId"/> that its blocks changed (the document's live room,
    /// <c>DocumentChangedExternally</c>), so it reloads them without a refresh.
    /// </summary>
    Task NotifyDocumentChangedAsync(Guid documentId, IReadOnlyList<Guid> blockIds, CancellationToken ct = default);

    /// <summary>Whether the account is staff (<c>Entitlements:StaffUserIds</c>): the bulk import timings are theirs to read.</summary>
    bool IsStaff(string userId);
}

/// <summary>
/// What a chunk became: its review session (the import's record), the blocks placed, and its warnings. With how long
/// its parts took, in ms (21 Nov 2026): the LaTeX read and staged (parse and cleanup), the figures stored as assets, and
/// the blocks written into the document.
/// </summary>
public sealed record PdfChunkPlacement(Guid SessionId, IReadOnlyList<Guid> BlockIds, int Warnings,
    int? ParseMs = null, int? FiguresMs = null, int? WriteMs = null);

/// <summary>
/// Compiles a bulk import's finished document once, in the background, to measure it (21 Nov 2026, the owner: "how long
/// it takes to actually compile"): the editor's normal compile path (Typst or LaTeX and the engine the document would
/// use), through the machine's compile slots. Implemented in Lilia.Api; tests put a fake in.
/// </summary>
public interface IBulkImportCompiler
{
    Task<BulkImportCompileResult> CompileAsync(Guid documentId, CancellationToken ct = default);
}

/// <summary>Whether a PDF came out, the engine that made it (or was tried), and the error's first line when it failed.</summary>
public sealed record BulkImportCompileResult(bool Ok, string? Engine, string? Error);
