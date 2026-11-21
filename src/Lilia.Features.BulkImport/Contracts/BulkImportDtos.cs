namespace Lilia.Api.Features.BulkImport.Contracts;

// The HTTP and SignalR contract of bulk import (21 Nov 2026). The web is built against these names: keep them.

/// <summary>A file's chunks: how many, how many placed in the document, how many couldn't be read.</summary>
public sealed record BulkImportChunkCountsDto(int Total, int Done, int Failed);

/// <summary>
/// How long a file's import took, in ms (21 Nov 2026), each null until measured: the upload (received → stored), the
/// split, the first page shown and the whole (both from the upload), Mathpix's ms per page read (cache hits left out),
/// the place per chunk, the wait per chunk before its reading started (the queue and the fairness hold), the background
/// compile (its result and engine), and the pages placed per minute of the whole. <c>CachedChunks</c>: chunks whose
/// reading came from the cache (no Mathpix time). Since staging (21 Nov 2026): <c>FirstPageMs</c> is to the first chunk
/// staged (shown in the preview), <c>PlaceMsPerChunk</c> a chunk's staging, and <c>PromoteMs</c> the staging made into the
/// document in one go.
/// </summary>
public sealed record BulkImportTimingsDto(
    int? UploadMs,
    int? SplitMs,
    int? FirstPageMs,
    int? TotalMs,
    int? MathpixMsPerPage,
    int? TexZipMsPerChunk,
    int? PlaceMsPerChunk,
    int? QueueWaitMsPerChunk,
    int? CompileMs,
    bool? CompileOk,
    string? CompileEngine,
    double? PagesPerMinute,
    int CachedChunks,
    int? PromoteMs = null);

/// <summary>
/// How sure the reader (Mathpix) was of the file's pages, 0–1 (the mean of its pages' scores), and the pages under
/// <c>BulkImport:LowConfidence</c> to check against the scan (21 Nov 2026).
/// </summary>
public sealed record BulkImportPrecisionDto(double Score, IReadOnlyList<int> LowPages);

/// <summary>
/// One PDF of a batch and where it stands. <c>PagesDone</c> counts per page: the pages of staged (or placed) chunks plus
/// the pages Mathpix has read so far of chunks in flight. <c>DocumentId</c> is null until promotion (21 Nov 2026): the
/// import is staged, and the document is made in one go when every chunk is finished; until then
/// <c>PreviewAvailable</c> (true from the split, its placeholders shown at once) says the read-only preview
/// (<c>GET …/files/{fileId}/preview</c>) has something to show. <c>PromotedAt</c>: when the document was made. A file
/// split before staging had its document from the split. <c>Warnings</c> is the count of the import's warnings (a page
/// the reader was unsure of is one). <c>Timings</c> and <c>Precision</c>: how long it took, and how sure the reader was
/// (null until a page has a score).
/// </summary>
public sealed record BulkImportFileDto(
    Guid FileId,
    string Name,
    int Pages,
    int PagesDone,
    int PagesFailed,
    string Status,
    BulkImportChunkCountsDto Chunks,
    Guid? DocumentId,
    string? Error,
    int Warnings,
    BulkImportTimingsDto? Timings = null,
    BulkImportPrecisionDto? Precision = null,
    bool PreviewAvailable = false,
    DateTime? PromotedAt = null);

/// <summary>
/// A block of the read-only preview (21 Nov 2026): a staged block (<c>Id</c>, its <c>Type</c> and <c>Content</c> as a
/// block's, its <c>ChunkIndex</c>), or, for a chunk not staged yet, <c>Type</c> <c>"placeholder"</c> with no id and
/// <c>Content</c> <c>{ firstPage, lastPage, state }</c>, state <c>queued</c>, <c>reading</c> or <c>failed</c> (a cancelled
/// chunk says failed: not read).
/// </summary>
public sealed record BulkImportPreviewBlockDto(Guid? Id, string Type, System.Text.Json.JsonElement Content, int ChunkIndex);

/// <summary>
/// <c>GET /api/bulk-imports/{batchId}/files/{fileId}/preview</c> (21 Nov 2026, author only): the file's staged blocks in
/// page order with a placeholder for each chunk not staged yet. Once promoted (<c>DocumentId</c> set) the staging is gone
/// and the blocks are empty: open the document.
/// </summary>
public sealed record BulkImportPreviewDto(
    Guid FileId,
    string Name,
    int Pages,
    int PagesDone,
    string Status,
    Guid? DocumentId,
    IReadOnlyList<BulkImportPreviewBlockDto> Blocks);

/// <summary>Sent to the author's group as <c>BulkImportPreviewChanged</c> when a chunk is staged or fails (21 Nov 2026).</summary>
public sealed record BulkImportPreviewChangedDto(Guid BatchId, Guid FileId, int ChunkIndex);

/// <summary>One stage's spread over the window (<c>GET /api/bulk-imports/stats</c>): how many measured, p50, p90, min, max.</summary>
public sealed record BulkImportStageStatsDto(string Stage, string Unit, int Count, double? P50, double? P90, double? Min, double? Max);

/// <summary>
/// Staff only (<c>GET /api/bulk-imports/stats?days=7</c>, 21 Nov 2026): every file uploaded in the last <c>Days</c> days
/// and its chunks, counted, and each stage's spread.
/// </summary>
public sealed record BulkImportStatsDto(
    int Days,
    DateTime From,
    DateTime To,
    int Files,
    int FilesFinished,
    int Chunks,
    int CacheHits,
    int Pages,
    int CompilesOk,
    int CompilesFailed,
    int LowPrecisionPages,
    IReadOnlyList<BulkImportStageStatsDto> Stages);

/// <summary>A batch with its files (<c>GET /api/bulk-imports/{batchId}</c>).</summary>
public sealed record BulkImportBatchDto(
    Guid BatchId,
    string Status,
    DateTime CreatedAt,
    int PagesTotal,
    int PagesDone,
    int PagesFailed,
    IReadOnlyList<BulkImportFileDto> Files);

/// <summary>A batch in the author's list (<c>GET /api/bulk-imports</c>).</summary>
public sealed record BulkImportBatchSummaryDto(
    Guid BatchId,
    string Status,
    DateTime CreatedAt,
    int PagesTotal,
    int PagesDone,
    int FilesTotal,
    int FilesDone,
    int FilesFailed);

/// <summary>The answer to <c>POST /api/bulk-imports</c>.</summary>
public sealed record BulkImportCreatedDto(Guid BatchId);

/// <summary>Sent to the author's group (<c>user:{id}</c>) on <c>/hubs/bulk-import</c> as <c>BulkImportProgress</c>.</summary>
public sealed record BulkImportProgressDto(Guid BatchId, BulkImportFileDto File);

/// <summary>
/// One warning of a file's import (21 Nov 2026, the launch bot's retest: "It never names the warning or the block"): its
/// <c>Kind</c> (the diagnostic's code, e.g. <c>MathPrintedAsText</c>, <c>FigureImageMissing</c>,
/// <c>PDF.OCR_LOW_PRECISION</c>; its category when it has no code), its <c>Severity</c>, a plain <c>Message</c> (without the
/// chunk-local "Block N (type): " or "Page N: " the import wrote, since <c>BlockNumber</c> and <c>Page</c> say where), and
/// where: the chunk's pages (<c>FirstPage</c>–<c>LastPage</c>, the file's), the page itself when the warning is about one,
/// and the block. <c>BlockId</c> is the block's id in the preview and, once the file is promoted, in the document;
/// <c>InDocument</c> says the block is in the document now (not deleted since), and <c>BlockNumber</c> is then its place
/// there, from 1. Null for a warning about a page or the whole chunk.
/// </summary>
public sealed record BulkImportWarningDto(
    string Kind,
    string Severity,
    string Message,
    int ChunkIndex,
    int FirstPage,
    int LastPage,
    int? Page,
    Guid? BlockId,
    string? BlockType,
    bool InDocument,
    int? BlockNumber);

/// <summary>
/// <c>GET /api/bulk-imports/{batchId}/files/{fileId}/warnings</c> (21 Nov 2026, author only): the file's warnings in page
/// order, read from its chunks' import diagnostics. <c>Total</c> is the file's count (its <c>warnings</c>); a file read
/// before a chunk kept its import record (a file split before staging) may list fewer. A file imported before 21 Nov 2026
/// counted its tex.zip's inventory ("Found N image file(s)") as warnings: the list leaves those out, and <c>Total</c>
/// with them, so it can be lower than the file's stored count. At most
/// <c>Limit</c> are listed; <c>Truncated</c> says there were more.
/// </summary>
public sealed record BulkImportWarningListDto(
    Guid FileId,
    Guid? DocumentId,
    int Total,
    bool Truncated,
    IReadOnlyList<BulkImportWarningDto> Warnings)
{
    public const int Limit = 500;
}
