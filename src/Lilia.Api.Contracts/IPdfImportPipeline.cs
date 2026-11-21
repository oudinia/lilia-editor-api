namespace Lilia.Api.Services;

/// <summary>
/// What the bulk import slice (<c>Lilia.Features.BulkImport</c>, 21 Nov 2026) uses of the single PDF import, so a PDF
/// dropped in bulk is counted, checked, charged and imported exactly as one uploaded alone: the same page count, the same
/// plan and credit checks, the same charge, and the same LaTeX project import (the LaTeX import, the cleanup of Mathpix's
/// reading, its figures stored as the document's assets). The difference: a chunk's blocks go into the file's existing
/// document, in place of the chunk's placeholder, rather than making a document of their own. Implemented in Lilia.Api
/// over <c>PdfPages</c>, <c>IEntitlementService</c>, the connector's credits, <c>JobService</c> and
/// <c>ImportReviewService</c>.
/// <para>Since 21 Nov 2026 a chunk is staged (<see cref="StageLatexProjectAsync"/>): the same import, its blocks and
/// figures returned rather than written into a document, so the import never writes into the author's document while it
/// runs. The file's document is made from them at the end (<see cref="ApplyStagedPreambleAsync"/> for its preamble).
/// <see cref="PlaceLatexProjectAsync"/> stays for a chunk retried after that, and for files split before staging.</para>
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
    /// A chunk's LaTeX project zip imported exactly as <see cref="PlaceLatexProjectAsync"/> imports it (its own import job,
    /// the LaTeX import, the cleanup of Mathpix's reading), but no document is touched: its blocks come back, each with
    /// the id it will keep, and its files are stored under <paramref name="assetKeyPrefix"/> (<c>bulk-staging/{fileId}/</c>),
    /// a figure's <c>src</c> pointing at <paramref name="assetUrlPrefix"/> + the file's name and its <c>stagedAssetId</c>
    /// at it. <see cref="PdfChunkStaged.Preamble"/> is the LaTeX before <c>\begin{document}</c>. Throws when the reading
    /// has no LaTeX; files stored before a failure are deleted.
    /// </summary>
    Task<PdfChunkStaged> StageLatexProjectAsync(
        string userId, string fileName, byte[] projectZip, string? sourcePdfKey,
        string assetKeyPrefix, string assetUrlPrefix, CancellationToken ct = default);

    /// <summary>
    /// A staged import's preamble onto its new document, as a single import's (class, packages, macros, page setup and
    /// venue), then <paramref name="extraMacros"/> (later chunks' macros) where the preamble lacks them. In the caller's
    /// transaction (the same database context).
    /// </summary>
    Task ApplyStagedPreambleAsync(Guid documentId, string ownerId, string? preamble, string? extraMacros, CancellationToken ct = default);

    /// <summary>The macro and environment definitions of <paramref name="latex"/> that <paramref name="have"/> doesn't define yet, or null.</summary>
    string? MissingDefinitions(string? have, string? latex);

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
/// A chunk staged (21 Nov 2026): its review session, its blocks in order (each with the id it keeps in the document, its
/// content as JSON), its files stored under the staging prefix, its preamble, its warnings, and how long the LaTeX read
/// (parse and cleanup) and the files' storing took, in ms.
/// </summary>
public sealed record PdfChunkStaged(Guid SessionId, IReadOnlyList<StagedBlockData> Blocks, IReadOnlyList<StagedAssetData> Assets,
    string? Preamble, int Warnings, int? ParseMs = null, int? FiguresMs = null);

/// <summary>A staged block: the id it keeps, its type, its content (a block's, as JSON) and depth.</summary>
public sealed record StagedBlockData(Guid Id, string Type, string ContentJson, int Depth);

/// <summary>A staged file: the asset id it keeps, its name in staging URLs, its path in the zip, where it is stored, its type and size.</summary>
public sealed record StagedAssetData(Guid Id, string Name, string OriginalPath, string StorageKey, string ContentType, long Size);

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
