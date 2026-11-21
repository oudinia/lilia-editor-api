namespace Lilia.Api.Features.BulkImport.Contracts;

// The HTTP and SignalR contract of bulk import (21 Nov 2026). The web is built against these names: keep them.

/// <summary>A file's chunks: how many, how many placed in the document, how many couldn't be read.</summary>
public sealed record BulkImportChunkCountsDto(int Total, int Done, int Failed);

/// <summary>
/// One PDF of a batch and where it stands. <c>PagesDone</c> counts per page: the pages of placed chunks plus the pages
/// Mathpix has read so far of chunks in flight. <c>DocumentId</c> is set as soon as the file is split: the document can be
/// opened at once and grows. <c>Warnings</c> is the count of the import's warnings.
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
    int Warnings);

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
